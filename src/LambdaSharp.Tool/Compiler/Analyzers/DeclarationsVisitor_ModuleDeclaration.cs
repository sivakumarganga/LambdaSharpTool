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
using System.Linq;
using LambdaSharp.Tool.Compiler.Parser.Syntax;

namespace LambdaSharp.Tool.Compiler.Analyzers {

    public partial class DeclarationsVisitor {

        //--- Methods ---
        public override void VisitStart(ASyntaxNode parent, ModuleDeclaration node) {

            // ensure module version is present and valid
            if(node.Version == null) {
                _builder.ModuleVersion = VersionInfo.Parse("1.0-DEV");
            } else if(VersionInfo.TryParse(node.Version.Value, out var version)) {
                _builder.ModuleVersion = version;
            } else {
                _builder.LogError($"'Version' expected to have format: Major.Minor[.Patch]", node.Version.SourceLocation);
                _builder.ModuleVersion = VersionInfo.Parse("0.0");
            }

            // ensure module has a namespace and name
            if(TryParseModuleFullName(node.Module.Value, out string moduleNamespace, out var moduleName)) {
                _builder.ModuleNamespace = moduleNamespace;
                _builder.ModuleName = moduleName;
            } else {
                _builder.LogError($"'Module' attribute must have format 'Namespace.Name'", node.Module.SourceLocation);
            }

            // validate secrets
            foreach(var secret in node.Secrets) {
                if(secret.Value.Equals("aws/ssm", StringComparison.OrdinalIgnoreCase)) {
                    _builder.LogError($"cannot grant permission to decrypt with aws/ssm", secret.SourceLocation);
                } else if(secret.Value.StartsWith("arn:", StringComparison.Ordinal)) {
                    if(!SecretArnRegex.IsMatch(secret.Value)) {
                        _builder.LogError("secret key must be a valid ARN", secret.SourceLocation);
                    }
                } else if(SecretAliasRegex.IsMatch(secret.Value)) {

                    // TODO: resolve KMS key alias to ARN

                    // // assume key name is an alias and resolve it to its ARN
                    // try {
                    //     var response = Settings.KmsClient.DescribeKeyAsync(textSecret).Result;
                    //     _secrets.Add(response.KeyMetadata.Arn);
                    //     return true;
                    // } catch(Exception e) {
                    //     LogError($"failed to resolve key alias: {textSecret}", e);
                    //     return false;
                    // }
                } else {
                    _builder.LogError($"secret key must be a valid alias", secret.SourceLocation);
                }
            }

            // add implicit module variables
            var moduleGroupDeclaration = AddDeclaration(node, new GroupDeclaration {
                Group = Literal("Module"),
                Description = Literal("Module Variables")
            });
            AddDeclaration(moduleGroupDeclaration, new VariableDeclaration {
                Variable = Literal("Id"),
                Description = Literal("Module ID"),
                Value = FnRef("AWS::StackName")
            });
            AddDeclaration(moduleGroupDeclaration, new VariableDeclaration {
                Variable = Literal("Namespace"),
                Description = Literal("Module Namespace"),
                Value = Literal(_builder.ModuleNamespace)
            });
            AddDeclaration(moduleGroupDeclaration, new VariableDeclaration {
                Variable = Literal("Name"),
                Description = Literal("Module Name"),
                Value = Literal(_builder.ModuleName)
            });
            AddDeclaration(moduleGroupDeclaration, new VariableDeclaration {
                Variable = Literal("FullName"),
                Description = Literal("Module Full Name"),
                Value = Literal(_builder.ModuleFullName)
            });
            AddDeclaration(moduleGroupDeclaration, new VariableDeclaration {
                Variable = Literal("Version"),
                Description = Literal("Module Version"),
                Value = Literal(_builder.ModuleVersion.ToString())
            });
            AddDeclaration(moduleGroupDeclaration, new ConditionDeclaration {
                Condition = Literal("IsNested"),
                Description = Literal("Module is nested"),
                Value = FnNot(FnEquals(FnRef("DeploymentRoot"), Literal("")))
            });
            AddDeclaration(moduleGroupDeclaration, new VariableDeclaration {
                Variable = Literal("RootId"),
                Description = Literal("Root Module ID"),
                Value = FnIf("Module::IsNested", FnRef("DeploymentRoot"), FnRef("Module::Id"))
            });

            // create module IAM role used by all functions
            AddDeclaration(moduleGroupDeclaration, new ResourceDeclaration {
                Resource = Literal("Role"),
                Type = Literal("AWS::IAM::Role"),
                Properties = new ObjectExpression {
                    ["AssumeRolePolicyDocument"] = new ObjectExpression {
                        ["Version"] = Literal("2012-10-17"),
                        ["Statement"] = new ListExpression {
                            new ObjectExpression {
                                ["Sid"] = Literal("ModuleLambdaPrincipal"),
                                ["Effect"] = Literal("Allow"),
                                ["Principal"] = new ObjectExpression {
                                    ["Service"] = Literal("lambda.amazonaws.com")
                                },
                                ["Action"] = Literal("sts:AssumeRole")
                            }
                        }
                    },
                    ["Policies"] = new ListExpression {
                        new ObjectExpression {
                            ["PolicyName"] = FnSub("${AWS::StackName}ModulePolicy"),
                            ["PolicyDocument"] = new ObjectExpression {
                                ["Version"] = Literal("2012-10-17"),
                                ["Statement"] = new ListExpression()
                            }
                        }
                    }
                },
                DiscardIfNotReachable = true
            });

            // add overridable logging retention variable
            if(!TryGetOverride(node, "Module::LogRetentionInDays", out var logRetentionInDays)) {
                logRetentionInDays = Literal(30);
            }
            AddDeclaration(moduleGroupDeclaration, new VariableDeclaration {
                Variable = Literal("LogRetentionInDays"),
                Description = Literal("Number days log entries are retained for"),
                Type = Literal("Number"),
                Value = logRetentionInDays
            });

            // add LambdaSharp Module Options
            var section = "LambdaSharp Module Options";
            AddDeclaration(node, new ParameterDeclaration {
                Parameter = Literal("Secrets"),
                Section = Literal(section),
                Label = Literal("Comma-separated list of additional KMS secret keys"),
                Description = Literal("Secret Keys (ARNs)"),
                Default = Literal("")
            });
            AddDeclaration(node, new ParameterDeclaration {
                Parameter = Literal("XRayTracing"),
                Section = Literal(section),
                Label = Literal("Enable AWS X-Ray tracing mode for module resources"),
                Description = Literal("AWS X-Ray Tracing"),
                Default = Literal(XRayTracingLevel.Disabled.ToString()),
                AllowedValues = new List<LiteralExpression> {
                    Literal(XRayTracingLevel.Disabled.ToString()),
                    Literal(XRayTracingLevel.RootModule.ToString()),
                    Literal(XRayTracingLevel.AllModules.ToString())
                },
                DiscardIfNotReachable = true
            });

            // TODO (2019-11-05, bjorg): consider making this a child declaration of the parameter XRayTracing::IsEnabled
            AddDeclaration(node, new ConditionDeclaration {
                Condition = Literal("XRayIsEnabled"),
                Value = FnNot(FnEquals(FnRef("XRayTracing"), Literal(XRayTracingLevel.Disabled.ToString())))
            });

            // TODO (2019-11-05, bjorg): consider making this a child declaration of the parameter XRayTracing::NestedIsEnabled
            AddDeclaration(node, new ConditionDeclaration {
                Condition = Literal("XRayNestedIsEnabled"),
                Value = FnEquals(FnRef("XRayTracing"), Literal(XRayTracingLevel.AllModules.ToString()))
            });

            // check if module might depdent on core services
            if(HasLambdaSharpDependencies(node) || HasModuleRegistration(node)) {
                AddDeclaration(node, new ParameterDeclaration {
                    Parameter = Literal("LambdaSharpCoreServices"),
                    Section = Literal(section),
                    Label = Literal("Integrate with LambdaSharp.Core services"),
                    Description = Literal("Use LambdaSharp.Core Services"),

                    // TODO (2019-11-05, bjorg): use enum with ToString() instead of hard-coded strings
                    Default = Literal("Disabled"),
                    AllowedValues = new List<LiteralExpression> {
                        Literal("Disabled"),
                        Literal("Enabled")
                    },
                    DiscardIfNotReachable = true
                });
                AddDeclaration(node, new ConditionDeclaration {
                    Condition = Literal("UseCoreServices"),

                    // TODO (2019-11-05, bjorg): use enum with ToString() instead of hard-coded strings
                    Value = FnEquals(FnRef("LambdaSharpCoreServices"), Literal("Enabled"))
                });
            }

            // import lambdasharp dependencies (unless requested otherwise)
            if(HasLambdaSharpDependencies(node)) {

                // add LambdaSharp Module Internal resource imports
                var lambdasharpGroupDeclaration = AddDeclaration(node, new GroupDeclaration {
                    Group = Literal("LambdaSharp"),
                    Description = Literal("LambdaSharp Core Imports")
                });
                AddDeclaration(lambdasharpGroupDeclaration, new ImportDeclaration {
                    Import = Literal("DeadLetterQueue"),
                    Module = Literal("LambdaSharp.Core"),

                    // TODO (2018-12-01, bjorg): consider using 'AWS::SQS::Queue'
                    Type = Literal("String")
                });
                AddDeclaration(lambdasharpGroupDeclaration, new ImportDeclaration {
                    Import = Literal("LoggingStream"),
                    Module = Literal("LambdaSharp.Core"),

                    // NOTE (2018-12-11, bjorg): we use type 'String' to be more flexible with the type of values we're willing to take
                    Type = Literal("String")
                });
                AddDeclaration(lambdasharpGroupDeclaration, new ImportDeclaration {
                    Import = Literal("LoggingStreamRole"),
                    Module = Literal("LambdaSharp.Core"),

                    // NOTE (2018-12-11, bjorg): we use type 'String' to be more flexible with the type of values we're willing to take
                    Type = Literal("String")
                });
            }

            // add module variables
            if(TryGetVariable(node, "DeadLetterQueue", out var deadLetterQueueVariable, out var deadLetterQueueCondition)) {
                AddDeclaration(moduleGroupDeclaration, new VariableDeclaration {
                    Variable = Literal("DeadLetterQueue"),
                    Description = Literal("Module Dead Letter Queue (ARN)"),
                    Value = deadLetterQueueVariable
                });
                AddGrant(
                    name: "DeadLetterQueue",
                    awsType: null,
                    reference: FnRef("Module::DeadLetterQueue"),
                    allow: new[] {
                        "sqs:SendMessage"
                    },
                    condition: deadLetterQueueCondition
                );
            }
            if(TryGetVariable(node, "LoggingStream", out var loggingStreamVariable, out var _)) {
                AddDeclaration(moduleGroupDeclaration, new VariableDeclaration {
                    Variable = Literal("LoggingStream"),
                    Description = Literal("Module Logging Stream (ARN)"),
                    Value = loggingStreamVariable,

                    // TODO (2019-11-05, bjorg): can we use a more specific type than 'String' here?
                    Type = Literal("String")
                });
            }
            if(TryGetVariable(node, "LoggingStreamRole", out var loggingStreamRoleVariable, out var _)) {
                AddDeclaration(moduleGroupDeclaration, new VariableDeclaration {
                    Variable = Literal("LoggingStreamRole"),
                    Description = Literal("Module Logging Stream Role (ARN)"),
                    Value = loggingStreamRoleVariable,

                    // TODO (2019-11-05, bjorg): consider using 'AWS::IAM::Role'
                    Type = Literal("String")

                });
            }

            // add KMS permissions for secrets in module
            if(node.Secrets.Any()) {
                AddGrant(
                    name: "EmbeddedSecrets",
                    awsType: null,
                    reference: new ListExpression {
                        Items = node.Secrets.Cast<AExpression>().ToList()
                    },
                    allow: new[] {
                        "kms:Decrypt",
                        "kms:Encrypt"
                    },
                    condition: null
                );
            }

            // add decryption function for secret parameters and values
            AddDeclaration(node, new FunctionDeclaration {
                Function = Literal("DecryptSecretFunction"),
                Description = Literal("Module secret decryption function"),
                Environment = new ObjectExpression {

                    // NOTE (2019-11-05, bjorg): we use the Lambda environment to introduce a conditional dependency
                    //  on the policy for KMS keys passed in through the 'Secrets' parameter; without this dependency,
                    //  the Lambda function could run before the policy is in effect, causing it to fail.
                    ["MODULE_ROLE_SECRETSPOLICY"] = FnIf(
                        "Module::Role::SecretsPolicy::Condition",
                        FnRef("Module::Role::SecretsPolicy"),
                        FnRef("AWS::NoValue")
                    )
                },
                Pragmas = new ListExpression {
                    Literal("no-function-registration"),
                    Literal("no-dead-letter-queue"),
                    Literal("no-wildcard-scoped-variables")
                },
                Timeout = Literal(30),
                Memory = Literal(128),
                Runtime = Literal("nodejs8.10"),
                Handler = Literal("index.handler"),
                Language = Literal("javascript"),
                Properties = new ObjectExpression {
                    ["Code"] = new ObjectExpression {
                        ["ZipFile"] = Literal(_decryptSecretFunctionCode)
                    }
                },
                DiscardIfNotReachable = true
            });

            // add LambdaSharp Deployment Settings
            section = "LambdaSharp Deployment Settings (DO NOT MODIFY)";
            AddDeclaration(node, new ParameterDeclaration {
                Parameter = Literal("DeploymentBucketName"),
                Section = Literal(section),
                Label = Literal("Deployment S3 bucket name"),
                Description = Literal("Deployment S3 Bucket Name")
            });
            AddDeclaration(node, new ParameterDeclaration {
                Parameter = Literal("DeploymentPrefix"),
                Section = Literal(section),
                Label = Literal("Deployment tier prefix"),
                Description = Literal("Deployment Tier Prefix")
            });
            AddDeclaration(node, new ParameterDeclaration {
                Parameter = Literal("DeploymentPrefixLowercase"),
                Section = Literal(section),
                Label = Literal("Deployment tier prefix (lowercase)"),
                Description = Literal("Deployment Tier Prefix (lowercase)")
            });
            AddDeclaration(node, new ParameterDeclaration {
                Parameter = Literal("DeploymentRoot"),
                Section = Literal(section),
                Label = Literal("Root stack name for nested deployments, blank otherwise"),
                Description = Literal("Root Stack Name"),
                Default = Literal("")
            });
            AddDeclaration(node, new ParameterDeclaration {
                Parameter = Literal("DeploymentChecksum"),
                Section = Literal(section),
                Label = Literal("CloudFormation template MD5 checksum"),
                Description = Literal("Deployment Checksum"),
                Default = Literal("")
            });

            // add conditional KMS permissions for secrets parameter
            AddGrant(
                name: "Secrets",
                awsType: null,
                reference: FnSplit(",", FnRef("Secrets")),
                allow: new List<string> {
                    "kms:Decrypt",
                    "kms:Encrypt"
                },
                condition: FnNot(FnEquals(FnRef("Secrets"), Literal("")))
            );

            // permissions needed for writing to log streams (but not for creating log groups!)
            AddGrant(
                name: "LogStream",
                awsType: null,
                reference: Literal("arn:aws:logs:*:*:*"),
                allow: new[] {
                    "logs:CreateLogStream",
                    "logs:PutLogEvents"
                },
                condition: null
            );

            // permissions needed for reading state of CloudFormation stack (used by Finalizer to confirm a delete operation is happening)
            AddGrant(
                name: "CloudFormation",
                awsType: null,
                reference: FnRef("AWS::StackId"),
                allow: new[] {
                    "cloudformation:DescribeStacks"
                },
                condition: null
            );

            // permissions needed for X-Ray lambda daemon to upload tracing information
            AddGrant(
                name: "AWSXRay",
                awsType: null,
                reference: Literal("*"),
                allow: new[] {
                    "xray:PutTraceSegments",
                    "xray:PutTelemetryRecords",
                    "xray:GetSamplingRules",
                    "xray:GetSamplingTargets",
                    "xray:GetSamplingStatisticSummaries"
                },
                condition: null
            );

            // add module registration
            if(HasModuleRegistration(node)) {
                _builder.AddSharedDependency(node, new ModuleInfo("LambdaSharp", "Core", _builder.CoreServicesReferenceVersion, "lambdasharp"));

                // create module registration
                AddDeclaration(node, new ResourceDeclaration {
                    Resource = Literal("Registration"),
                    Type = Literal("LambdaSharp::Registration::Module"),
                    Properties = new ObjectExpression {
                        ["Module"] = Literal(_builder.ModuleInfo.ToString()),
                        ["ModuleId"] = FnRef("AWS::StackName")
                    },
                    If = FnCondition("UseCoreServices")
                });
            }
        }

        public override void VisitEnd(ASyntaxNode parent, ModuleDeclaration node) {

            // permissions needed for lambda functions to exist in a VPC
            if(_builder.ItemDeclarations.OfType<FunctionDeclaration>().Any()) {
                AddGrant(
                    name: "VpcNetworkInterfaces",
                    awsType: null,
                    reference: Literal("*"),
                    allow: new[] {
                        "ec2:DescribeNetworkInterfaces",
                        "ec2:CreateNetworkInterface",
                        "ec2:DeleteNetworkInterface"
                    },
                    condition: null
                );
            }

            // TODO: this might be a good spot to compute the effective role permissions
        }
    }
}