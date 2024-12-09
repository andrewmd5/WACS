// /*
//  * Copyright 2024 Kelvin Nishikawa
//  *
//  * Licensed under the Apache License, Version 2.0 (the "License");
//  * you may not use this file except in compliance with the License.
//  * You may obtain a copy of the License at
//  *
//  *     http://www.apache.org/licenses/LICENSE-2.0
//  *
//  * Unless required by applicable law or agreed to in writing, software
//  * distributed under the License is distributed on an "AS IS" BASIS,
//  * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  * See the License for the specific language governing permissions and
//  * limitations under the License.
//  */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types.Defs;

namespace Spec.Test.WastJson
{
    public class DebuggerBreak : ICommand
    {
        public CommandType Type { get; }
        public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime runtime, ref Module? module)
        {
            Debugger.Break();
            return new();
        }
    }
    
    public class ModuleCommand : ICommand
    {
        private SpecTestEnv _env = new SpecTestEnv();
        [JsonPropertyName("filename")] public string? Filename { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }

        public CommandType Type => CommandType.Module;
        [JsonPropertyName("line")] public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime runtime, ref Module? module)
        {
            List<Exception> errors = new();

            var filepath = Path.Combine(testDefinition.Path, Filename!);
            using var fileStream = new FileStream(filepath, FileMode.Open);
            module = BinaryModuleParser.ParseWasm(fileStream);
            var modInst = runtime.InstantiateModule(module);
            var moduleName = !string.IsNullOrEmpty(Name)?Name:$"{filepath}";
            module.SetName(moduleName);
            
            if (!string.IsNullOrEmpty(Name))
                modInst.Name = Name;
            
            return errors;
        }

        public override string ToString() => $"Load Module {{ Line = {Line}, Filename = {Filename} }}";
    }

    public class RegisterCommand : ICommand
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("as")] public string? As { get; set; }
        public CommandType Type => CommandType.Module;
        [JsonPropertyName("line")] public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime runtime, ref Module? module)
        {
            List<Exception> errors = new();
            if (As == null)
                throw new ArgumentException("Json missing `as` field");
            
            var modInst = runtime.GetModule(Name);
            runtime.RegisterModule(As, modInst);
            return errors;
        }

        public override string ToString() => $"Register Module {{ Line = {Line}, Name = {Name}, As = {As} }}";
    }
    
    public class ActionCommand : ICommand
    {
        [JsonPropertyName("action")] public IAction? Action { get; set; }
        public CommandType Type => CommandType.Action;
        [JsonPropertyName("line")] public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime runtime, ref Module? module)
        {
            List<Exception> errors = new();
            switch (Action)
            {
                case InvokeAction invokeAction:
                    invokeAction.Invoke(ref runtime, ref module);
                    break;
            }
            return errors;
        }

        public override string ToString() => $"Action {{ Line = {Line}, Action = {Action} }}";
    }

    public class AssertReturnCommand : ICommand
    {
        [JsonPropertyName("action")] public IAction? Action { get; set; }
        [JsonPropertyName("expected")] public List<Argument> Expected { get; set; } = new();
        public CommandType Type => CommandType.AssertReturn;
        [JsonPropertyName("line")] public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime runtime, ref Module? module)
        {
            List<Exception> errors = new();
            
            switch (Action)
            {
                case InvokeAction invokeAction:
                    var result = invokeAction.Invoke(ref runtime, ref module);
                    
                    if (result.Length != Expected.Count)
                        throw new TestException(
                            $"Test failed {this} \"{invokeAction.Field}\": Expected [{string.Join(" ", Expected.Select(e => e.AsValue))}], but got [{string.Join(" ", result)}]");

                    foreach (var (actual, expected) in result.Zip(Expected, (a, e) => (a, e.AsValue)))
                    {
                        //HACK: null ref comparison
                        if (expected.IsNullRef)
                        {
                            if (!actual.IsNullRef && !expected.Type.Matches(actual.Type, null))
                                throw new TestException(
                                    $"Test failed {this} \"{invokeAction.Field}\": Expected [{string.Join(" ", Expected.Select(e => e.AsValue))}], but got [{string.Join(" ", result)}]");
                        }
                        else if (!actual.Equals(expected))
                        {
                            throw new TestException(
                                $"Test failed {this} \"{invokeAction.Field}\": Expected [{string.Join(" ", Expected.Select(e => e.AsValue))}], but got [{string.Join(" ", result)}]");    
                        }
                    }                    
                    break;
            }
            return errors;
        }

        public override string ToString() => $"Assert Return {{ Line = {Line}, Action = {Action}, Expected = [{string.Join(", ", Expected)}] }}";
    }
    
    public class AssertTrapCommand : ICommand
    {
        [JsonPropertyName("action")] public IAction? Action { get; set; }
        [JsonPropertyName("text")] public string? Text { get; set; }
        public CommandType Type => CommandType.AssertTrap;
        [JsonPropertyName("line")] public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime runtime, ref Module? module)
        {
            List<Exception> errors = new();
            
            switch (Action)
            {
                case InvokeAction invokeAction:
                    bool didTrap = false;
                    string trapMessage = "";
                    try
                    {
                        var result = invokeAction.Invoke(ref runtime, ref module);
                    }
                    catch (TrapException e)
                    {
                        didTrap = true;
                        trapMessage = e.Message;
                    }

                    if (!didTrap)
                        throw new TestException($"Test failed {this} \"{trapMessage}\"");
                    break;
            }

            return errors;
        }

        public override string ToString() => $"Assert Trap {{ Line = {Line}, Action = {Action}, Text = {Text} }}";
    }

    public class AssertExhaustionCommand : ICommand
    {
        [JsonPropertyName("action")] public IAction? Action { get; set; }
        [JsonPropertyName("text")] public string? Text { get; set; }
        public CommandType Type => CommandType.AssertExhaustion;
        [JsonPropertyName("line")] public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime runtime, ref Module? module)
        {
            List<Exception> errors = new();
            
            switch (Action)
            {
                case InvokeAction invokeAction:
                    //Compute type from action.Args and action.Expected
                    bool didThrow = false;
                    string throwMessage = "";
                    try
                    {
                        var result = invokeAction.Invoke(ref runtime, ref module);
                    }
                    catch (WasmRuntimeException)
                    {
                        didThrow = true;
                        throwMessage = Text ?? "";
                    }
                    if (!didThrow)
                        throw new TestException($"Test failed {this} \"{throwMessage}\"");
                    break;
            }
            return errors;
        }

        public override string ToString() => $"Assert Exhaustion {{ Line = {Line}, Action = {Action} }}";
    }

    public class AssertInvalidCommand : ICommand
    {
        [JsonPropertyName("filename")] public string? Filename { get; set; }
        [JsonPropertyName("module_type")] public string? ModuleType { get; set; }
        [JsonPropertyName("text")] public string? Text { get; set; }
        public CommandType Type => CommandType.AssertInvalid;
        [JsonPropertyName("line")] public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime runtime, ref Module? module)
        {
            List<Exception> errors = new();
            if (ModuleType == "text")
            {
                errors.Add(new Exception(
                    $"Assert Malformed line {Line}: Skipping assert_malformed. No WAT parsing."));
                return errors;
            }
            
            if (Filename == null)
                throw new ArgumentException("Json missing `filename` field");
            
            var filepath = Path.Combine(testDefinition.Path, Filename);
            bool didAssert = false;
            string assertionMessage = "";
            try
            {
                using var fileStream = new FileStream(filepath, FileMode.Open);
                var stubmodule = BinaryModuleParser.ParseWasm(fileStream);
                stubmodule.SetName(filepath);
                var modInstInvalid = runtime.InstantiateModule(stubmodule);
            }
            catch (ValidationException exc)
            {
                didAssert = true;
                assertionMessage = exc.Message;
            }
            catch (InvalidDataException exc)
            {
                didAssert = true;
                assertionMessage = exc.Message;
            }
            catch (FormatException exc)
            {
                didAssert = true;
                assertionMessage = exc.Message;
            }

            if (!didAssert)
            {
                throw new TestException($"Test failed {this}");
            }

            return errors;
        }

        public override string ToString() => $"Assert Invalid {{ Line = {Line}, Filename = {Filename}, ModuleType = {ModuleType}, Text = {Text} }}";
    }

    public class AssertMalformedCommand : ICommand
    {
        [JsonPropertyName("filename")] public string? Filename { get; set; }
        [JsonPropertyName("text")] public string? Text { get; set; }
        [JsonPropertyName("module_type")] public string? ModuleType { get; set; }
        public CommandType Type => CommandType.AssertMalformed;
        [JsonPropertyName("line")] public int Line { get; set; }


        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime runtime, ref Module? module)
        {
            var errors = new List<Exception>();
            if (ModuleType == "text")
            {
                errors.Add(new Exception(
                    $"Assert Malformed line {Line}: Skipping assert_malformed. No WAT parsing."));
                return errors;
            }
            
            if (Filename == null)
                throw new ArgumentException("Json missing `filename` field");
            
            var filepath = Path.Combine(testDefinition.Path, Filename);
            bool didAssert = false;
            try
            {
                using var fileStream = new FileStream(filepath, FileMode.Open);
                var stubmodule = BinaryModuleParser.ParseWasm(fileStream);
                stubmodule.SetName(filepath);
                var modInstInvalid = runtime.InstantiateModule(stubmodule);
            }
            catch (FormatException)
            {
                didAssert = true;
            }
            catch (NotSupportedException)
            {
                didAssert = true;
            }
            catch (ValidationException)
            {
                didAssert = true;
            }

            if (!didAssert)
            {
                throw new TestException($"Test failed {this}");
            }

            return errors;
        }

        public override string ToString() => $"Assert Malformed {{ Line = {Line}, Filename = {Filename}, Text = {Text}, ModuleType = {ModuleType} }}";
    }

    public class AssertUnlinkableCommand : ICommand
    {
        [JsonPropertyName("filename")] public string? Filename { get; set; }
        [JsonPropertyName("text")] public string? Text { get; set; }
        [JsonPropertyName("module_type")] public string? ModuleType { get; set; }
        public CommandType Type => CommandType.AssertUnlinkable;
        [JsonPropertyName("line")] public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime runtime, ref Module? module)
        {
            List<Exception> errors = new();
            if (ModuleType == "text")
            {
                errors.Add(new Exception(
                    $"Assert Malformed line {Line}: Skipping assert_malformed. No WAT parsing."));
                return errors;
            }
            if (Filename == null)
                throw new ArgumentException("Json missing `filename` field");
            
            var filepath = Path.Combine(testDefinition.Path, Filename);
            bool didAssert = false;
            try
            {
                using var fileStream = new FileStream(filepath, FileMode.Open);
                var stubmodule = BinaryModuleParser.ParseWasm(fileStream);
                stubmodule.SetName(filepath);
                var modInstInvalid = runtime.InstantiateModule(stubmodule);
            }
            catch (NotSupportedException)
            {
                didAssert = true;
            }

            if (!didAssert)
            {
                throw new TestException($"Test failed {this}");
            }

            return errors;
        }

        public override string ToString() => $"Assert Unlinkable {{ Line = {Line}, Filename = {Filename}, Text = {Text}, ModuleType = {ModuleType} }}";
    }

    public class AssertUninstantiableCommand : ICommand
    {
        [JsonPropertyName("filename")] public string? Filename { get; set; }
        [JsonPropertyName("module_type")] public string? ModuleType { get; set; }
        [JsonPropertyName("text")] public string? Text { get; set; }
        public CommandType Type => CommandType.AssertUninstantiable;
        [JsonPropertyName("line")] public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime runtime, ref Module? module)
        {
            List<Exception> errors = new();
            
            if (Filename == null)
                throw new ArgumentException("Json missing `filename` field");
            
            var filepath = Path.Combine(testDefinition.Path, Filename);
            bool didAssert = false;
            try
            {
                using var fileStream = new FileStream(filepath, FileMode.Open);
                var stubmodule = BinaryModuleParser.ParseWasm(fileStream);
                stubmodule.SetName(filepath);
                var modInst = runtime.InstantiateModule(stubmodule);
            }
            catch (ValidationException)
            {
                didAssert = true;
            }
            catch (InvalidDataException)
            {
                didAssert = true;
            }
            catch (FormatException)
            {
                didAssert = true;
            }
            catch (TrapException)
            {
                didAssert = true;
            }
            
            if (!didAssert)
            {
                throw new TestException($"Test failed {this}");
            }

            return errors;
        }

        public override string ToString() => $"Assert Uninstantiable {{ Line = {Line}, Filename = {Filename}, ModuleType = {ModuleType}, Text = {Text} }}";
    }

    public class InvokeCommand : ICommand
    {
        [JsonPropertyName("module")] public string? Module { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("args")] public List<object> Args { get; set; } = new List<object>();
        public CommandType Type => CommandType.Invoke;
        [JsonPropertyName("line")] public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime runtime, ref Module? module)
        {
            throw new InvalidDataException($"Test command not setup:{this} from {testDefinition.TestName}");
        }

        public override string ToString() => $"Invoke {{ Line = {Line}, Module = {Module}, Name = {Name}, Args = [{string.Join(", ", Args)}] }}";
    }

    public class GetCommand : ICommand
    {
        [JsonPropertyName("module")] public string? Module { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        public CommandType Type => CommandType.Get;
        [JsonPropertyName("line")] public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime runtime, ref Module? module)
        {
            throw new InvalidDataException($"Test command not setup:{this} from {testDefinition.TestName}");
        }

        public override string ToString() => $"Get {{ Line = {Line}, Module = {Module}, Name = {Name} }}";
    }

    public class SetCommand : ICommand
    {
        [JsonPropertyName("module")] public string? Module { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("value")] public object? Value { get; set; }
        public CommandType Type => CommandType.Set;
        [JsonPropertyName("line")] public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime runtime, ref Module? module)
        {
            throw new InvalidDataException($"Test command not setup:{this} from {testDefinition.TestName}");
        }

        public override string ToString() => $"Set {{ Line = {Line}, Module = {Module}, Name = {Name}, Value = {Value} }}";
    }

    public class StartCommand : ICommand
    {
        [JsonPropertyName("module")] public string? Module { get; set; }
        public CommandType Type => CommandType.Start;
        [JsonPropertyName("line")] public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime runtime, ref Module? module)
        {
            throw new InvalidDataException($"Test command not setup:{this} from {testDefinition.TestName}");
        }

        public override string ToString() => $"Start {{ Line = {Line}, Module = {Module} }}";
    }

    public class AssertReturnCanonicalNansCommand : ICommand
    {
        [JsonPropertyName("action")] public IAction? Action { get; set; }
        [JsonPropertyName("expected")] public List<object> Expected { get; set; } = new List<object>();
        public CommandType Type => CommandType.AssertReturnCanonicalNans;
        [JsonPropertyName("line")] public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime runtime, ref Module? module)
        {
            throw new InvalidDataException($"Test command not setup:{this} from {testDefinition.TestName}");
        }

        public override string ToString() => $"Assert Return (Canonical Nans) {{ Line = {Line}, Action = {Action}, Expected = [{string.Join(", ", Expected)}] }}";
    }

    public class AssertReturnArithmeticNansCommand : ICommand
    {
        [JsonPropertyName("action")] public IAction? Action { get; set; }
        [JsonPropertyName("expected")] public List<object> Expected { get; set; } = new List<object>();
        public CommandType Type => CommandType.AssertReturnArithmeticNans;
        [JsonPropertyName("line")] public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime runtime, ref Module? module)
        {
            throw new InvalidDataException($"Test command not setup:{this} from {testDefinition.TestName}");
        }

        public override string ToString() => $"Assert Return (ArithmeticNans) {{ Action = {Action}, Expected = [{string.Join(", ", Expected)}] }}";
    }

    public class AssertReturnDetachedCommand : ICommand
    {
        [JsonPropertyName("action")] public IAction? Action { get; set; }
        public CommandType Type => CommandType.AssertReturnDetached;
        [JsonPropertyName("line")] public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime runtime, ref Module? module)
        {
            throw new InvalidDataException($"Test command not setup:{this} from {testDefinition.TestName}");
        }

        public override string ToString() => $"Assert Return (Detached) {{ Action = {Action} }}";
    }

    public class AssertTerminatedCommand : ICommand
    {
        [JsonPropertyName("module")] public string? Module { get; set; }
        public CommandType Type => CommandType.AssertTerminated;
        [JsonPropertyName("line")] public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime runtime, ref Module? module)
        {
            throw new InvalidDataException($"Test command not setup:{this} from {testDefinition.TestName}");
        }

        public override string ToString() => $"Assert Terminated {{ Module = {Module} }}";
    }

    public class AssertUndefinedCommand : ICommand
    {
        [JsonPropertyName("module")] public string? Module { get; set; }
        public CommandType Type => CommandType.AssertUndefined;
        [JsonPropertyName("line")] public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime runtime, ref Module? module)
        {
            throw new InvalidDataException($"Test command not setup:{this} from {testDefinition.TestName}");
        }

        public override string ToString() => $"Assert Undefined {{ Module = {Module} }}";
    }

    public class AssertExcludeFromMustCommand : ICommand
    {
        [JsonPropertyName("module")] public string? Module { get; set; }
        public CommandType Type => CommandType.AssertExcludeFromMust;
        [JsonPropertyName("line")] public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime runtime, ref Module? module)
        {
            throw new InvalidDataException($"Test command not setup:{this} from {testDefinition.TestName}");
        }

        public override string ToString() => $"Assert Exclude From Must {{ Line = {Line}, Module = {Module} }}";
    }

    public class ModuleInstanceCommand : ICommand
    {
        [JsonPropertyName("module")] public string? Module { get; set; }
        public CommandType Type => CommandType.ModuleInstance;
        [JsonPropertyName("line")] public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime runtime, ref Module? module)
        {
            throw new InvalidDataException($"Test command not setup:{this} from {testDefinition.TestName}");
        }

        public override string ToString() => $"Module Instance {{ Line = {Line}, Module = {Module} }}";
    }

    public class ModuleExclusiveCommand : ICommand
    {
        [JsonPropertyName("module")] public string? Module { get; set; }
        public CommandType Type => CommandType.ModuleExclusive;
        [JsonPropertyName("line")] public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime runtime, ref Module? module)
        {
            throw new InvalidDataException($"Test command not setup:{this} from {testDefinition.TestName}");
        }

        public override string ToString() => $"Module Exclusive {{ Line = {Line}, Module = {Module} }}";
    }

    public class PumpCommand : ICommand
    {
        [JsonPropertyName("action")] public IAction? Action { get; set; }
        public CommandType Type => CommandType.Pump;
        [JsonPropertyName("line")] public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime runtime, ref Module? module)
        {
            throw new InvalidDataException($"Test command not setup:{this} from {testDefinition.TestName}");
        }

        public override string ToString() => $"Pump {{ Line = {Line}, Action = {Action} }}";
    }

    public class MaybeCommand : ICommand
    {
        [JsonPropertyName("command")] public ICommand? Command { get; set; }
        public CommandType Type => CommandType.Maybe;
        [JsonPropertyName("line")] public int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime runtime, ref Module? module)
        {
            throw new InvalidDataException($"Test command not setup:{this} from {testDefinition.TestName}");
        }

        public override string ToString() => $"Maybe {{ Line = {Line}, Command = {Command} }}";
    }
    
    public class CommandJsonConverter : JsonConverter<ICommand>
    {
        public override ICommand? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions? options)
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            JsonElement root = document.RootElement;

            string? typeString = root.GetProperty("type").GetString();
            CommandType type = EnumHelper.GetEnumValueFromString<CommandType>(typeString);

            ICommand? command = type switch {
                CommandType.DebuggerBreak =>JsonSerializer.Deserialize<DebuggerBreak>(root.GetRawText(), options), 
                CommandType.Module => JsonSerializer.Deserialize<ModuleCommand>(root.GetRawText(), options),
                CommandType.Register => JsonSerializer.Deserialize<RegisterCommand>(root.GetRawText(), options),
                CommandType.Action => JsonSerializer.Deserialize<ActionCommand>(root.GetRawText(), options),
                CommandType.AssertReturn => JsonSerializer.Deserialize<AssertReturnCommand>(root.GetRawText(), options),
                CommandType.AssertTrap => JsonSerializer.Deserialize<AssertTrapCommand>(root.GetRawText(), options),
                CommandType.AssertExhaustion => JsonSerializer.Deserialize<AssertExhaustionCommand>(root.GetRawText(), options),
                CommandType.AssertInvalid => JsonSerializer.Deserialize<AssertInvalidCommand>(root.GetRawText(), options),
                CommandType.AssertMalformed => JsonSerializer.Deserialize<AssertMalformedCommand>(root.GetRawText(), options),
                CommandType.AssertUnlinkable => JsonSerializer.Deserialize<AssertUnlinkableCommand>(root.GetRawText(), options),
                CommandType.AssertUninstantiable => JsonSerializer.Deserialize<AssertUninstantiableCommand>(root.GetRawText(), options),
                CommandType.Invoke => JsonSerializer.Deserialize<InvokeCommand>(root.GetRawText(), options),
                CommandType.Get => JsonSerializer.Deserialize<GetCommand>(root.GetRawText(), options),
                CommandType.Set => JsonSerializer.Deserialize<SetCommand>(root.GetRawText(), options),
                CommandType.Start => JsonSerializer.Deserialize<StartCommand>(root.GetRawText(), options),
                CommandType.AssertReturnCanonicalNans => JsonSerializer.Deserialize<AssertReturnCanonicalNansCommand>(root.GetRawText(), options),
                CommandType.AssertReturnArithmeticNans => JsonSerializer.Deserialize<AssertReturnArithmeticNansCommand>(root.GetRawText(), options),
                CommandType.AssertReturnDetached => JsonSerializer.Deserialize<AssertReturnDetachedCommand>(root.GetRawText(), options),
                CommandType.AssertTerminated => JsonSerializer.Deserialize<AssertTerminatedCommand>(root.GetRawText(), options),
                CommandType.AssertUndefined => JsonSerializer.Deserialize<AssertUndefinedCommand>(root.GetRawText(), options),
                CommandType.AssertExcludeFromMust => JsonSerializer.Deserialize<AssertExcludeFromMustCommand>(root.GetRawText(), options),
                CommandType.ModuleInstance => JsonSerializer.Deserialize<ModuleInstanceCommand>(root.GetRawText(), options),
                CommandType.ModuleExclusive => JsonSerializer.Deserialize<ModuleExclusiveCommand>(root.GetRawText(), options),
                CommandType.Pump => JsonSerializer.Deserialize<PumpCommand>(root.GetRawText(), options),
                CommandType.Maybe => JsonSerializer.Deserialize<MaybeCommand>(root.GetRawText(), options),
                
                _ => throw new NotSupportedException($"Command type '{type}' is not supported.")
            };

            return command;
        }

        public override void Write(Utf8JsonWriter writer, ICommand value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, (object)value, options);
        }
    }
}