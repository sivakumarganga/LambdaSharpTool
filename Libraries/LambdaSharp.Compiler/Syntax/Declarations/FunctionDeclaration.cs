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
using System.Linq;
using LambdaSharp.Compiler.Syntax.EventSources;
using LambdaSharp.Compiler.Syntax.Expressions;

namespace LambdaSharp.Compiler.Syntax.Declarations {

    [SyntaxDeclarationKeyword("Function")]
    public sealed class FunctionDeclaration :
        AItemDeclaration,
        IScopedDeclaration,
        IConditionalResourceDeclaration,
        IInitializedResourceDeclaration
    {

        //--- Types ---
        public class VpcExpression : ASyntaxNode {

            //--- Fields ---
            private AExpression? _securityGroupIds;
            private AExpression? _subnetIds;

            //--- Properties ---

            [SyntaxRequired]
            public AExpression? SecurityGroupIds {
                get => _securityGroupIds;
                set => _securityGroupIds = SetParent(value);
            }

            [SyntaxRequired]
            public AExpression? SubnetIds {
                get => _subnetIds;
                set => _subnetIds = SetParent(value);
            }
        }

        //--- Fields ---
        private SyntaxNodeCollection<LiteralExpression> _scope;
        private AExpression? _if;
        private AExpression? _memory;
        private AExpression? _timeout;
        private LiteralExpression? _project;
        private LiteralExpression? _runtime;
        private LiteralExpression? _language;
        private LiteralExpression? _handler;
        private VpcExpression? _vpc;
        private ObjectExpression _environment;
        private ObjectExpression _properties;
        private SyntaxNodeCollection<AEventSourceDeclaration> _sources;
        private ListExpression _pragmas;

        //--- Constructors ---
        public FunctionDeclaration(LiteralExpression itemName) : base(itemName) {
            _scope = SetParent(new SyntaxNodeCollection<LiteralExpression>());
            _environment = SetParent(new ObjectExpression());
            _properties = SetParent(new ObjectExpression());
            _sources = new SyntaxNodeCollection<AEventSourceDeclaration>();
            _pragmas = SetParent(new ListExpression());
        }

        //--- Properties ---

        [SyntaxOptional]
        public SyntaxNodeCollection<LiteralExpression> Scope {
            get => _scope;
            set => _scope = SetParent(value ?? throw new ArgumentNullException());
        }

        [SyntaxOptional]
        public AExpression? If {
            get => _if;
            set => _if = SetParent(value);
        }

        [SyntaxRequired]
        public AExpression? Memory {
            get => _memory;
            set => _memory = SetParent(value);
        }

        [SyntaxRequired]
        public AExpression? Timeout {
            get => _timeout;
            set => _timeout = SetParent(value);
        }

        [SyntaxOptional]
        public LiteralExpression? Project {
            get => _project;
            set => _project = SetParent(value);
        }

        [SyntaxOptional]
        public LiteralExpression? Runtime {
            get => _runtime;
            set => _runtime = SetParent(value);
        }

        [SyntaxOptional]
        public LiteralExpression? Language {
            get => _language;
            set => _language = SetParent(value);
        }

        [SyntaxOptional]
        public LiteralExpression? Handler {
            get => _handler;
            set => _handler = SetParent(value);
        }

        // TODO (2020-01-30, bjorg): this notation is deprecated, use `VpcConfig` in `Properties` instead
        [SyntaxOptional]
        public VpcExpression? Vpc {
            get => _vpc;
            set => _vpc = SetParent(value);
        }

        [SyntaxOptional]
        public ObjectExpression Environment {
            get => _environment;
            set => _environment = SetParent(value);
        }

        [SyntaxOptional]
        public ObjectExpression Properties {
            get => _properties;
            set => _properties = SetParent(value);
        }

        [SyntaxOptional]
        public SyntaxNodeCollection<AEventSourceDeclaration> Sources {
            get => _sources;
            set => _sources = SetParent(value);
        }

        [SyntaxOptional]
        public ListExpression Pragmas {
            get => _pragmas;
            set => _pragmas = SetParent(value);
        }

        public string CloudFormationType => "AWS::Lambda::Function";

        public bool HasPragma(string pragma) => Pragmas.Any(expression => (expression is LiteralExpression literalExpression) && (literalExpression.Value == pragma));
        public bool HasDeadLetterQueue => !HasPragma("no-dead-letter-queue");
        public bool HasAssemblyValidation => !HasPragma("no-assembly-validation");
        public bool HasHandlerValidation => !HasPragma("no-handler-validation");
        public bool HasWildcardScopedVariables => !HasPragma("no-wildcard-scoped-variables");
        public bool HasFunctionRegistration => !HasPragma("no-function-registration");
        public bool HasTypeValidation => !HasPragma("no-type-validation");
        public bool HasSecretType => false;
        public string? IfConditionName => ((ConditionReferenceExpression?)If)?.ReferenceName!.Value;
        public LiteralExpression? Type => Fn.Literal("AWS::Lambda::Function");

        //--- IInitializedResourceDeclaration Members ---
        LiteralExpression? IInitializedResourceDeclaration.ResourceTypeName => Type;
        bool IInitializedResourceDeclaration.HasInitialization => true;
        ObjectExpression? IInitializedResourceDeclaration.InitializationExpression => Properties;
    }
}