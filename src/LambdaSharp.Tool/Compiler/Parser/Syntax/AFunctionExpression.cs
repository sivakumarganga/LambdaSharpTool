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

namespace LambdaSharp.Tool.Compiler.Parser.Syntax {

    public abstract class AFunctionExpression : AExpression { }

    public class Base64FunctionExpression : AFunctionExpression {

        //--- Fields ---
        private AExpression? _value;

        // !Base64 VALUE
        // NOTE: You can use any function that returns a string inside the Fn::Base64 function.

        //--- Properties ---
        public AExpression Value {
            get => _value ?? throw new InvalidOperationException();
            set => _value = SetParent(value) ?? throw new ArgumentNullException();
        }

        //--- Methods ---
        public override void Visit(ASyntaxNode parent, ISyntaxVisitor visitor) {
            visitor.VisitStart(parent, this);
            Value?.Visit(this, visitor);
            visitor.VisitEnd(parent, this);
        }
    }

    public class CidrFunctionExpression : AFunctionExpression {

        // !Cidr [ VALUE, VALUE, VALUE ]
        // NOTE: You can use the following functions in a Fn::Cidr function:
        //  - !Select
        //  - !Ref

        //--- Fields ---
        private AExpression? _ipBlock;
        private AExpression? _count;
        private AExpression? _cidrBits;

        //--- Properties ---
        public AExpression IpBlock {
            get => _ipBlock ?? throw new InvalidOperationException();
            set => _ipBlock = SetParent(value) ?? throw new ArgumentNullException();
        }

        public AExpression Count {
            get => _count ?? throw new InvalidOperationException();
            set => _count = SetParent(value) ?? throw new ArgumentNullException();
        }

        public AExpression CidrBits {
            get => _cidrBits ?? throw new InvalidOperationException();
            set => _cidrBits = SetParent(value) ?? throw new ArgumentNullException();
        }

        //--- Methods ---
        public override void Visit(ASyntaxNode parent, ISyntaxVisitor visitor) {
            visitor.VisitStart(parent, this);
            IpBlock?.Visit(this, visitor);
            Count?.Visit(this, visitor);
            CidrBits?.Visit(this, visitor);
            visitor.VisitEnd(parent, this);
        }
    }

    public class FindInMapFunctionExpression : AFunctionExpression {

        // !FindInMap [ STRING, VALUE, VALUE ]
        // NOTE: You can use the following functions in a Fn::FindInMap function:
        //  - Fn::FindInMap
        //  - Ref

        //--- Fields ---
        private LiteralExpression? _mapName;
        private AExpression? _topLevelKey;
        private AExpression? _secondLevelKey;

        //--- Properties ---

        // TODO: allow AExpression, but then validate that after optimization it is a string literal
        public LiteralExpression MapName {
            get => _mapName ?? throw new InvalidOperationException();
            set => _mapName = SetParent(value) ?? throw new ArgumentNullException();
        }

        public AExpression TopLevelKey {
            get => _topLevelKey ?? throw new InvalidOperationException();
            set => _topLevelKey = SetParent(value) ?? throw new ArgumentNullException();
        }

        public AExpression SecondLevelKey {
            get => _secondLevelKey ?? throw new InvalidOperationException();
            set => _secondLevelKey = SetParent(value) ?? throw new ArgumentNullException();
        }
        public MappingDeclaration? ReferencedDeclaration { get; set; }

        //--- Methods ---
        public override void Visit(ASyntaxNode parent, ISyntaxVisitor visitor) {
            visitor.VisitStart(parent, this);
            MapName?.Visit(this, visitor);
            TopLevelKey?.Visit(this, visitor);
            SecondLevelKey?.Visit(this, visitor);
            visitor.VisitEnd(parent, this);
        }
    }

    public class GetAttFunctionExpression : AFunctionExpression {

        // !GetAtt [ STRING, VALUE ]
        // NOTE: For the Fn::GetAtt logical resource name, you cannot use functions. You must specify a string that is a resource's logical ID.
        // For the Fn::GetAtt attribute name, you can use the Ref function.

        //--- Fields ---
        private LiteralExpression? _referenceName;
        private AExpression? _attributeName;
        private AItemDeclaration? referencedDeclaration;

        //--- Properties ---

        // TODO: allow AExpression, but then validate that after optimization it is a string literal
        public LiteralExpression ReferenceName {
            get => _referenceName ?? throw new InvalidOperationException();
            set => _referenceName = SetParent(value) ?? throw new ArgumentNullException();
        }

        public AExpression AttributeName {
            get => _attributeName ?? throw new InvalidOperationException();
            set => _attributeName = SetParent(value) ?? throw new ArgumentNullException();
        }

        public AItemDeclaration? ReferencedDeclaration {
            get => referencedDeclaration;
            set {
                if(referencedDeclaration != null) {
                    referencedDeclaration.UntrackDependency(this);
                }
                referencedDeclaration = value;
                if(referencedDeclaration != null) {
                    ParentItemDeclaration?.TrackDependency(referencedDeclaration, this);
                }
            }
        }

        //--- Methods ---
        public override void Visit(ASyntaxNode parent, ISyntaxVisitor visitor) {
            visitor.VisitStart(parent, this);
            ReferenceName?.Visit(this, visitor);
            AttributeName?.Visit(this, visitor);
            visitor.VisitEnd(parent, this);
        }
    }

    public class GetAZsFunctionExpression : AFunctionExpression {

        // !GetAZs VALUE
        // NOTE: You can use the Ref function in the Fn::GetAZs function.

        //--- Fields ---
        private AExpression? _region;

        //--- Properties ---
        public AExpression Region {
            get => _region ?? throw new InvalidOperationException();
            set => _region = SetParent(value) ?? throw new ArgumentNullException();
        }

        //--- Methods ---
        public override void Visit(ASyntaxNode parent, ISyntaxVisitor visitor) {
            visitor.VisitStart(parent, this);
            Region?.Visit(this, visitor);
            visitor.VisitEnd(parent, this);
        }
    }

    public class IfFunctionExpression : AFunctionExpression {

        // !If [ CONDITION, VALUE, VALUE ]
        // NOTE: AWS CloudFormation supports the Fn::If intrinsic function in the metadata attribute, update policy attribute, and property values in the Resources section and Outputs sections of a template.
        //  - Fn::Base64
        //  - Fn::FindInMap
        //  - Fn::GetAtt
        //  - Fn::GetAZs
        //  - Fn::If
        //  - Fn::Join
        //  - Fn::Select
        //  - Fn::Sub
        //  - Ref

        //--- Fields ---
        private AExpression? _condition;
        private AExpression? _ifTrue;
        private AExpression? _ifFalse;

        //--- Properties ---
        public AExpression Condition {
            get => _condition ?? throw new InvalidOperationException();
            set => _condition = SetParent(value) ?? throw new ArgumentNullException();
        }

        public AExpression IfTrue {
            get => _ifTrue ?? throw new InvalidOperationException();
            set => _ifTrue = SetParent(value) ?? throw new ArgumentNullException();
        }

        public AExpression IfFalse {
            get => _ifFalse ?? throw new InvalidOperationException();
            set => _ifFalse = SetParent(value) ?? throw new ArgumentNullException();
        }

        //--- Methods ---
        public override void Visit(ASyntaxNode parent, ISyntaxVisitor visitor) {
            visitor.VisitStart(parent, this);
            Condition?.Visit(this, visitor);
            IfTrue?.Visit(this, visitor);
            IfFalse?.Visit(this, visitor);
            visitor.VisitEnd(parent, this);
        }
    }

    public class ImportValueFunctionExpression : AFunctionExpression {

        // !ImportValue VALUE
        // NOTE: You can use the following functions in the Fn::ImportValue function. The value of these functions can't depend on a resource.
        //  - Fn::Base64
        //  - Fn::FindInMap
        //  - Fn::If
        //  - Fn::Join
        //  - Fn::Select
        //  - Fn::Split
        //  - Fn::Sub
        //  - Ref

        //--- Fields ---
        private AExpression? _sharedValueToImport;

        //--- Properties ---
        public AExpression SharedValueToImport {
            get => _sharedValueToImport ?? throw new InvalidOperationException();
            set => _sharedValueToImport = SetParent(value) ?? throw new ArgumentNullException();
        }

        //--- Methods ---
        public override void Visit(ASyntaxNode parent, ISyntaxVisitor visitor) {
            visitor.VisitStart(parent, this);
            SharedValueToImport?.Visit(this, visitor);
            visitor.VisitEnd(parent, this);
        }
    }

    public class JoinFunctionExpression : AFunctionExpression {

        // !Join [ STRING, VALUE ]
        // NOTE: For the Fn::Join delimiter, you cannot use any functions. You must specify a string value.
        //  For the Fn::Join list of values, you can use the following functions:
        //  - Fn::Base64
        //  - Fn::FindInMap
        //  - Fn::GetAtt
        //  - Fn::GetAZs
        //  - Fn::If
        //  - Fn::ImportValue
        //  - Fn::Join
        //  - Fn::Split
        //  - Fn::Select
        //  - Fn::Sub
        //  - Ref

        //--- Fields ---
        private LiteralExpression? _separator;
        private AExpression? _values;

        //--- Properties ---

        // TODO: allow AExpression, but then validate that after optimization it is a string literal
        public LiteralExpression Separator {
            get => _separator ?? throw new InvalidOperationException();
            set => _separator = SetParent(value) ?? throw new ArgumentNullException();
        }

        public AExpression Values {
            get => _values ?? throw new InvalidOperationException();
            set => _values = SetParent(value) ?? throw new ArgumentNullException();
        }

        //--- Methods ---
        public override void Visit(ASyntaxNode parent, ISyntaxVisitor visitor) {
            visitor.VisitStart(parent, this);
            Separator?.Visit(this, visitor);
            Values?.Visit(this, visitor);
            visitor.VisitEnd(parent, this);
        }
    }

    public class SelectFunctionExpression : AFunctionExpression {

        // !Select [ VALUE, VALUE ]
        // NOTE: For the Fn::Select index value, you can use the Ref and Fn::FindInMap functions.
        //  For the Fn::Select list of objects, you can use the following functions:
        //  - Fn::FindInMap
        //  - Fn::GetAtt
        //  - Fn::GetAZs
        //  - Fn::If
        //  - Fn::Split
        //  - Ref

        //--- Fields ---
        private AExpression? _index;
        private AExpression? _values;

        //--- Properties ---
        public AExpression Index {
            get => _index ?? throw new InvalidOperationException();
            set => _index = SetParent(value) ?? throw new ArgumentNullException();
        }

        public AExpression? Values {
            get => _values;
            set => _values = SetParent(value);
        }

        //--- Methods ---
        public override void Visit(ASyntaxNode parent, ISyntaxVisitor visitor) {
            visitor.VisitStart(parent, this);
            Index?.Visit(this, visitor);
            Values?.Visit(this, visitor);
            visitor.VisitEnd(parent, this);
        }
    }

    public class SplitFunctionExpression : AFunctionExpression {

        // !Split [ VALUE, VALUE ]
        // NOTE: For the Fn::Split delimiter, you cannot use any functions. You must specify a string value.
        //  For the Fn::Split list of values, you can use the following functions:
        //  - Fn::Base64
        //  - Fn::FindInMap
        //  - Fn::GetAtt
        //  - Fn::GetAZs
        //  - Fn::If
        //  - Fn::ImportValue
        //  - Fn::Join
        //  - Fn::Select
        //  - Fn::Sub
        //  - Ref

        //--- Fields ---
        private LiteralExpression? _delimiter;
        private AExpression? _sourceString;

        //--- Properties ---

        // TODO: allow AExpression, but then validate that after optimization it is a string literal
        public LiteralExpression Delimiter {
            get => _delimiter ?? throw new InvalidOperationException();
            set => _delimiter = SetParent(value) ?? throw new ArgumentNullException();
        }

        public AExpression SourceString {
            get => _sourceString ?? throw new InvalidOperationException();
            set => _sourceString = SetParent(value) ?? throw new ArgumentNullException();
        }

        //--- Methods ---
        public override void Visit(ASyntaxNode parent, ISyntaxVisitor visitor) {
            visitor.VisitStart(parent, this);
            Delimiter?.Visit(this, visitor);
            SourceString?.Visit(this, visitor);
            visitor.VisitEnd(parent, this);
        }
    }

    public class SubFunctionExpression : AFunctionExpression {

        // !Sub VALUE
        // !Sub [ VALUE, OBJECT ]
        // NOTE: For the String parameter, you cannot use any functions. You must specify a string value.
        // For the VarName and VarValue parameters, you can use the following functions:
        //  - Fn::Base64
        //  - Fn::FindInMap
        //  - Fn::GetAtt
        //  - Fn::GetAZs
        //  - Fn::If
        //  - Fn::ImportValue
        //  - Fn::Join
        //  - Fn::Select
        //  - Ref

        //--- Fields ---
        private LiteralExpression? _formatString;
        private ObjectExpression? _parameters;

        //--- Properties ---

        // TODO: allow AExpression, but then validate that after optimization it is a string literal
        public LiteralExpression FormatString {
            get => _formatString ?? throw new InvalidOperationException();
            set => _formatString = SetParent(value) ?? throw new ArgumentNullException();
        }

        public ObjectExpression Parameters {
            get => _parameters ?? throw new InvalidOperationException();
            set => _parameters = SetParent(value) ?? throw new ArgumentNullException();
        }

        //--- Methods ---
        public override void Visit(ASyntaxNode parent, ISyntaxVisitor visitor) {
            visitor.VisitStart(parent, this);
            FormatString?.Visit(this, visitor);
            Parameters?.Visit(this, visitor);
            visitor.VisitEnd(parent, this);
        }
    }

    public class TransformFunctionExpression : AFunctionExpression {

        // !Transform { Name: STRING, Parameters: OBJECT }
        // NOTE: AWS CloudFormation passes any intrinsic function calls included in Fn::Transform to the specified macro as literal strings.

        //--- Fields ---
        private LiteralExpression? _macroName;
        private ObjectExpression? _parameters;

        //--- Properties ---

        // TODO: allow AExpression, but then validate that after optimization it is a string literal
        public LiteralExpression MacroName {
            get => _macroName ?? throw new InvalidOperationException();
            set => _macroName = SetParent(value) ?? throw new ArgumentNullException();
        }

        public ObjectExpression Parameters {
            get => _parameters ?? throw new InvalidOperationException();
            set => _parameters = SetParent(value) ?? throw new ArgumentNullException();
        }

        //--- Methods ---
        public override void Visit(ASyntaxNode parent, ISyntaxVisitor visitor) {
            visitor.VisitStart(parent, this);
            MacroName?.Visit(this, visitor);
            Parameters?.Visit(this, visitor);
            visitor.VisitEnd(parent, this);
        }
    }

    public class ReferenceFunctionExpression : AFunctionExpression {

        // !Ref STRING
        // NOTE: You cannot use any functions in the Ref function. You must specify a string that is a resource logical ID.

        //--- Fields ---
        private LiteralExpression? _referenceName;
        private AItemDeclaration? _referencedDeclaration;

        //--- Properties ---

        // TODO: allow AExpression, but then validate that after optimization it is a string literal
        public LiteralExpression ReferenceName {
            get => _referenceName ?? throw new InvalidOperationException();
            set => _referenceName = SetParent(value) ?? throw new ArgumentNullException();
        }

        public AItemDeclaration ReferencedDeclaration {
            get => _referencedDeclaration ?? throw new InvalidOperationException();
            set => _referencedDeclaration = SetParent(value) ?? throw new ArgumentNullException();
        }

        //--- Methods ---
        public override void Visit(ASyntaxNode parent, ISyntaxVisitor visitor) {
            visitor.VisitStart(parent, this);
            ReferenceName?.Visit(this, visitor);
            visitor.VisitEnd(parent, this);
        }
    }
}