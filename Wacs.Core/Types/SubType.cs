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
using System.IO;

namespace Wacs.Core.Types
{
    public class SubType
    {
        public readonly bool Final;
        public readonly TypeIdx[] TypeIndexes;
        public readonly CompositeType CompType;

        public SubType(TypeIdx[] idxs, CompositeType cmpType, bool final)
        {
            TypeIndexes = idxs;
            CompType = cmpType;
            Final = final;
        }
        
        public SubType(CompositeType cmpType, bool final)
        {
            TypeIndexes = Array.Empty<TypeIdx>();
            CompType = cmpType;
            Final = final;
        }
        
        public static SubType Parse(BinaryReader reader)
        {
            return null;
        }
    }
}