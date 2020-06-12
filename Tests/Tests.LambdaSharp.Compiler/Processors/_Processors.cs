/*
 * LambdaSharp (λ#)
 * Copyright (C) 2018-2019
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

using System.Runtime.CompilerServices;
using LambdaSharp.Compiler.Syntax.Declarations;
using LambdaSharp.Compiler.Processors;
using Xunit.Abstractions;

namespace Tests.LambdaSharp.Compiler.Processors {

    public abstract class _Processor : _Init {

        //--- Constructors ---
        protected _Processor(ITestOutputHelper output) : base(output) { }

        //--- Methods ---
        protected ModuleDeclaration LoadTestModule([CallerMemberName] string testName = "") {

            // parse test source
            var result = NewParser($"@Processors/{GetType().Name}/{testName}.yml").ParseModule();
            ShouldNotBeNull(result);
            ExpectedMessages();

            // register declarations
            new PseudoParameterProcessor(this).Process(result);
            new ItemDeclarationProcessor(this).Process(result);
            ExpectedMessages();
            return result;
        }
    }
}