/*
 * LambdaSharp (λ#)
 * Copyright (C) 2018-2020
 * lambdasharp.net
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;

namespace LambdaSharp.Compiler.TypeSystem {

    internal class AnyResourceAttribute : IResourceAttribute {

        //--- Constructors ---
        public AnyResourceAttribute(string attributeName) => Name = attributeName ?? throw new ArgumentNullException(nameof(attributeName));

        //--- Properties ---
        public string Name { get; }
        public ResourceCollectionType CollectionType => ResourceCollectionType.NoCollection;
        public ResourceItemType ItemType => ResourceItemType.Any;
        public IResourceType ComplexType => AnyResourceType.Instance;
    }
}