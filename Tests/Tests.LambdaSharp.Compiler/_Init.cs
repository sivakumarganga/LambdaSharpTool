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

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using LambdaSharp.Compiler;
using LambdaSharp.Compiler.Parser;
using LambdaSharp.Compiler.Syntax.Declarations;
using LambdaSharp.Compiler.Validators;
using Xunit.Abstractions;

namespace Tests.LambdaSharp.Compiler {

    public abstract class _Init : IModuleValidatorDependencyProvider {

        //--- Types ---
        public class InMemoryLogger : ILogger {

            //--- Fields ---
            private readonly List<string> _messages;

            //--- Constructors ---
            public InMemoryLogger(List<string> messages) => _messages = messages ?? throw new ArgumentNullException(nameof(messages));

            //--- Properties ---
            public IEnumerable<string> Messages => _messages;

            //--- Methods ---
            public void Log(IBuildReportEntry entry, SourceLocation? sourceLocation, bool exact) {
                _messages.Add($"{entry.Severity.ToString().ToUpperInvariant()}{((entry.Code != 0) ? entry.Code.ToString() : "")}: {entry.Message} @ {sourceLocation?.FilePath ?? "<n/a>"}({sourceLocation?.LineNumberStart ?? 0},{sourceLocation?.ColumnNumberStart ?? 0})");
            }
        }

        public class ParserDependencyProvider : ILambdaSharpParserDependencyProvider {

            //--- Fields ---
            private readonly InMemoryLogger _logger;

            //--- Constructors ---
            public ParserDependencyProvider(List<string> messages) => _logger = new InMemoryLogger(messages ?? throw new ArgumentNullException(nameof(messages)));

            //--- Properties ---
            public IEnumerable<string> Messages => _logger.Messages;
            public Dictionary<string, string> Files { get; } = new Dictionary<string, string>();
            public ILogger Logger => _logger;

            //--- Methods ---
            public string ReadFile(string filePath) => Files[filePath];
        }

        // public class BuilderDependencyProvider : IBuilderDependencyProvider {

        //     //--- Fields ---
        //     private readonly List<string> _messages;

        //     //--- Constructors ---
        //     public BuilderDependencyProvider(List<string> messages) => _messages = messages ?? throw new ArgumentNullException(nameof(messages));

        //     //--- Properties ---
        //     public string ToolDataDirectory => Path.Combine(Environment.GetEnvironmentVariable("LAMBDASHARP") ?? throw new ApplicationException("missing LAMBDASHARP environment variable"), "Tests", "Tests.LambdaSharp.Tool-Test-Output");
        //     public IEnumerable<string> Messages => _messages;

        //     //--- Methods ---
        //     public async Task<string> GetS3ObjectContentsAsync(string bucketName, string key) {
        //         switch(bucketName) {
        //         case "lambdasharp":
        //             return GetType().Assembly.ReadManifestResource($"Resources/{key}");
        //         default:

        //             // nothing to do
        //             break;
        //         }
        //         return null;
        //     }

        //     public async Task<IEnumerable<string>> ListS3BucketObjects(string bucketName, string prefix) {
        //         switch(bucketName) {
        //         case "lambdasharp":
        //             switch(prefix) {
        //             case "lambdasharp/LambdaSharp/Core/":
        //                 return new[] {
        //                     "0.7.0"
        //                 };
        //             case "lambdasharp/LambdaSharp/S3.Subscriber/":
        //                 return new[] {
        //                     "0.7.3"
        //                 };
        //             default:

        //                 // nothing to do
        //                 break;
        //             }
        //             break;
        //         default:

        //             // nothing to do
        //             break;
        //         }
        //         return Enumerable.Empty<string>();
        //     }

        //     public void Log(IBuildReportEntry entry, SourceLocation sourceLocation, bool exact) {
        //         var label = entry.Severity.ToString().ToUpperInvariant();
        //         if(sourceLocation == null) {
        //             _messages.Add($"{label}{((entry.Code != 0) ? $" ({entry.Code})" : "")}: {entry.Message}");
        //         } else if(exact) {
        //             _messages.Add($"{label}{((entry.Code != 0) ? $" ({entry.Code})" : "")}: {entry.Message} @ {sourceLocation}");
        //         } else {
        //             _messages.Add($"{label}{((entry.Code != 0) ? $" ({entry.Code})" : "")}: {entry.Message} @ (near) {sourceLocation}");
        //         }
        //     }

        //     public async Task<CloudFormationSpec> ReadCloudFormationSpecAsync(RegionEndpoint region, VersionInfo version) {
        //         var assembly = GetType().Assembly;
        //         using(var specResource = assembly.GetManifestResourceStream($"{assembly.GetName().Name}.Resources.CloudFormationResourceSpecification.json.gz"))
        //         using(var specGzipStream = new GZipStream(specResource, CompressionMode.Decompress))
        //         using(var specReader = new StreamReader(specGzipStream)) {
        //             return JsonConvert.DeserializeObject<CloudFormationSpec>(specReader.ReadToEnd());
        //         }
        //     }
        // }

        //--- Class Methods ---
        protected static string ReadFromResources(string filename) {
            var assembly = typeof(_Init).Assembly;
            var resourceName = $"{assembly.GetName().Name}.Resources.{filename.Replace(" ", "_").Replace("\\", ".").Replace("/", ".")}";
            using var resource = assembly.GetManifestResourceStream(resourceName);

            // TODO: don't throw an exception; log an error instead
            using var reader = new StreamReader(resource ?? throw new Exception($"unable to locate embedded resource: '{resourceName}' in assembly '{assembly.GetName().Name}'"), Encoding.UTF8);
            return reader.ReadToEnd().Replace("\r", "");
        }

        //--- Fields ---
        protected readonly ITestOutputHelper Output;
        protected readonly ParserDependencyProvider Provider;
        protected readonly List<string> Messages = new List<string>();
        protected readonly Dictionary<string, AItemDeclaration> Declarations = new Dictionary<string, AItemDeclaration>();

        //--- Constructors ---
        public _Init(ITestOutputHelper output) {
            Output = output;
            Provider = new ParserDependencyProvider(Messages);
            Logger = new InMemoryLogger(Messages);
        }

        //--- Properties ---
        public ILogger Logger { get; }

        //--- Methods ---
        protected void AddSource(string filePath, string source) => Provider.Files.Add(filePath, source);

        protected LambdaSharpParser NewParser(string source) {
            if(source.StartsWith("@", StringComparison.Ordinal)) {
                source = ReadFromResources(source.Substring(1));
            }
            AddSource("test.yml", source);
            return new LambdaSharpParser(Provider, "test.yml");
        }

        protected LambdaSharpParser NewParser(string workdingDirectory, string filename) {
            return new LambdaSharpParser(Provider, workdingDirectory, filename);
        }

        protected void ExpectedMessages(params string[] expectedMessages) {
            var expected = new HashSet<string>(expectedMessages);
            var unexpected = Provider.Messages
                .Where(message => !expected.Contains(message))
                .ToList();
            foreach(var message in unexpected) {
                Output.WriteLine(message);
            }
            unexpected.Any().Should().Be(false);
        }

        //--- IModuleValidatorDependencyProvider Members ---
        bool IModuleValidatorDependencyProvider.IsValidResourceType(string type) {
            throw new NotImplementedException();
        }

        bool IModuleValidatorDependencyProvider.TryGetResourceType(string typeName, out ResourceType resourceType) {
            throw new NotImplementedException();
        }

        Task<string> IModuleValidatorDependencyProvider.ConvertKmsAliasToArn(string alias) {
            throw new NotImplementedException();
        }

        void IModuleValidatorDependencyProvider.DeclareItem(AItemDeclaration declaration)
            => Declarations.Add(declaration.FullName, declaration);

        bool IModuleValidatorDependencyProvider.TryGetItem(string fullname, [NotNullWhen(true)] out AItemDeclaration? itemDeclaration)
            => Declarations.TryGetValue(fullname, out itemDeclaration);
    }
}