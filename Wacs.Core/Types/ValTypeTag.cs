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

namespace Wacs.Core.Types
{
    [Flags]
    public enum ValTypeTag
    {
        Unknown = 0,
        Nil = 0b0000_0001,

        //Defaultable
        Default = 0b0000_1000,

        NumType = 0b0000_0100 | Default,
        VecType = 0b0000_0010 | Default,

        RefType = 0b0001_0000,
        IdxType = 0b0010_0000,
        NulType = 0b0100_0000,
        RefNull = RefType | NulType | Default,
        IdxRef = RefType | IdxType,
        IdxNull = RefNull | IdxType,

        Mask = 0b1111_1111,
        Block = 0b0001_0000_0000,
        ExecContext = 0b1000_0000_0000,
    }
}