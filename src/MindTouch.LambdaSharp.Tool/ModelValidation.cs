/*
 * MindTouch λ#
 * Copyright (C) 2018 MindTouch, Inc.
 * www.mindtouch.com  oss@mindtouch.com
 *
 * For community documentation and downloads visit mindtouch.com;
 * please review the licensing section.
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
using System.Text.RegularExpressions;
using MindTouch.LambdaSharp.Tool.Model.AST;

namespace MindTouch.LambdaSharp.Tool {

    public class ModelValidation : AModelProcessor {

        //--- Constants ---
        private const string SECRET_ALIAS_PATTERN = "[0-9a-zA-Z/_\\-]+";

        //--- Fields ---
        private ModuleNode _module;
        private HashSet<string> _names;

        //--- Constructors ---
        public ModelValidation(Settings settings, string sourceFilename) : base(settings, sourceFilename) { }

        //--- Methods ---
        public void Process(ModuleNode module) {
            Validate(module);
        }

        private void Validate(ModuleNode module) {
            _module = module;
            _names = new HashSet<string>();
            Validate(module.Module != null, "missing module name");

            // ensure collections are present
            module.Pragmas = module.Pragmas ?? new List<object>();
            module.Secrets = module.Secrets ?? new List<string>();
            module.Inputs = module.Inputs ?? new List<InputNode>();
            module.Variables = module.Variables ?? new List<ParameterNode>();
            module.Functions = module.Functions ?? new List<FunctionNode>();
            module.Outputs = module.Outputs ?? new List<OutputNode>();

            // ensure version is present
            if(module.Version == null) {
                module.Version = "1.0";
            } else if(!VersionInfo.TryParse(module.Version, out VersionInfo version)) {
                AddError("`Version` expected to have format: Major.Minor[.Build[.Revision]]");
                module.Version = "0.0";
            }

            // process data structures
            AtLocation("Secrets", () => ValidateSecrets(module.Secrets));
            AtLocation("Inputs", () => ValidateInputs(module.Inputs));
            AtLocation("Variables", () => ValidateParameters(module.Variables));
            AtLocation("Functions", () => ValidateFunctions(module.Functions));
            AtLocation("Outputs", () => ValidateOutputs(module.Outputs));
        }

        private void ValidateSecrets(IEnumerable<string> secrets) {
            var index = 0;
            foreach(var secret in secrets) {
                ++index;
                AtLocation($"[{index}]", () => {
                    if(string.IsNullOrEmpty(secret)) {
                        AddError($"secret has no value");
                    } else if(secret.Equals("aws/ssm", StringComparison.OrdinalIgnoreCase)) {
                        AddError($"cannot grant permission to decrypt with aws/ssm");
                    } else if(secret.StartsWith("arn:")) {
                        if(!Regex.IsMatch(secret, $"arn:aws:kms:{Settings.AwsRegion}:{Settings.AwsAccountId}:key/[a-fA-F0-9\\-]+")) {
                            AddError("secret key must be a valid ARN for the current region and account ID");
                        }
                    } else if(!Regex.IsMatch(secret, SECRET_ALIAS_PATTERN)) {
                        AddError("secret key must be a valid alias");
                    }
                });
            }
        }

        private void ValidateParameters(IEnumerable<ParameterNode> parameters, string prefix = "") {
            var index = 0;
            foreach(var parameter in parameters) {
                ++index;
                AtLocation(parameter.Var ?? parameter.Package ?? $"[{index}]", () => {
                    ValidateScope(parameter.Scope);
                    if(parameter.Secret != null) {
                        ValidateResourceName(parameter.Var, prefix);
                        ValidateNotBothStatements("Secret", "Resource", parameter.Resource == null);
                        ValidateNotBothStatements("Secret", "Value", parameter.Value == null);
                        ValidateNotBothStatements("Secret", "Package", parameter.Package == null);
                    } else if(parameter.Value != null) {
                        ValidateResourceName(parameter.Var, prefix);
                        ValidateNotBothStatements("Value", "Secret", parameter.Secret == null);
                        ValidateNotBothStatements("Value", "EncryptionContext", parameter.EncryptionContext == null);
                        ValidateNotBothStatements("Value", "Package", parameter.Package == null);
                    } else if(parameter.Package != null) {
                        ValidateResourceName(parameter.Package, prefix);
                        ValidateNotBothStatements("Package", "Resource", parameter.Resource == null);
                        ValidateNotBothStatements("Package", "Value", parameter.Value == null);
                        ValidateNotBothStatements("Package", "Secret", parameter.Secret == null);
                        ValidateNotBothStatements("Package", "EncryptionContext", parameter.EncryptionContext == null);

                        // check if required attributes are present
                        Validate(parameter.Files != null, "missing 'Files' attribute");
                        Validate(parameter.Bucket != null, "missing 'Bucket' attribute");
                        if(parameter.Bucket != null) {

                            // verify that target bucket is defined as parameter with correct type
                            ValidateSourceParameter(parameter.Bucket, "AWS::S3::Bucket", "Kinesis S3 bucket resource");
                        }

                        // check if package is nested
                        if(prefix != "") {
                            AddError("parameter package cannot be nested");
                        }
                    } else if(parameter.Resource != null) {
                        ValidateResourceName(parameter.Var, prefix);
                        ValidateNotBothStatements("Resource", "Secret", parameter.Secret == null);
                        ValidateNotBothStatements("Resource", "EncryptionContext", parameter.EncryptionContext == null);
                        ValidateNotBothStatements("Resource", "Package", parameter.Package == null);
                    } else if(parameter.Variables == null) {
                        AddError("unknown variable type");
                    }
                    if(parameter.Variables != null) {
                        AtLocation("Variables", () => {

                            // recursively validate nested parameters
                            ValidateParameters(parameter.Variables, prefix + "::" + parameter.Var);
                        });
                    }
                    if(parameter.Resource != null) {
                        AtLocation("Resource", () => ValidateResource(parameter, parameter.Resource));
                    }
                });
            }
        }

        private void ValidateResource(ParameterNode parameter, ResourceNode resource) {
            if(parameter.Value != null) {
                resource.Type = resource.Type ?? "AWS";
                ValidateNotBothStatements("Value", "Properties", resource.Properties == null);
                if(parameter.Value is string text) {
                    ValidateARN(text);
                } else if(parameter.Value is IList<object> values) {
                    foreach(var value in values) {
                        ValidateARN(value);
                    }
                }
            } else if(resource.Type == null) {
                AddError("missing Type attribute");
            } else if(
                resource.Type.StartsWith("AWS::", StringComparison.Ordinal)
                && !ResourceMapping.IsResourceTypeSupported(resource.Type)
            ) {
                AddError($"unsupported resource type: {resource.Type}");
            } else if(!resource.Type.StartsWith("AWS::", StringComparison.Ordinal)) {
                Validate(resource.Allow == null, "'Allow' attribute is not valid for custom resources");
            }

            // validate dependencies
            if(resource.DependsOn == null) {
                resource.DependsOn = new List<string>();
            } else {
                AtLocation("DependsOn", () => {
                    var dependencies = ConvertToStringList(resource.DependsOn);
                    foreach(var dependency in dependencies) {
                        var dependentParameter = _module.Variables.FirstOrDefault(p => p.Var == dependency);
                        if(dependentParameter == null) {
                            AddError($"could not find dependency '{dependency}'");
                        } else if(dependentParameter.Resource == null) {
                            AddError($"cannot depend on literal parameter '{dependency}'");
                        } else if(parameter.Var == dependency) {
                            AddError($"dependency cannot be on itself '{dependency}'");
                        }
                    }
                });
            }

            // local functions
            void ValidateARN(object resourceArn) {
                if((resourceArn is string text) && !text.StartsWith("arn:") && (text != "*")) {
                    AddError($"resource name must be a valid ARN or wildcard: {resourceArn}");
                }
            }
        }

        private void ValidateFunctions(IEnumerable<FunctionNode> functions) {
            if(!functions.Any()) {
                return;
            }

            // validate functions
            var index = 0;
            foreach(var function in functions) {
                ++index;
                AtLocation(function.Function ?? $"[{index}]", () => {
                    ValidateResourceName(function.Function, "");
                    Validate(function.Memory != null, "missing Memory attribute");
                    Validate(int.TryParse(function.Memory, out _), "invalid Memory value");
                    Validate(function.Timeout != null, "missing Name attribute");
                    Validate(int.TryParse(function.Timeout, out _), "invalid Timeout value");
                    Validate(function.PackagePath == null, "'PackagePath' is reserved for internal use");
                    function.Sources = function.Sources ?? new List<FunctionSourceNode>();
                    function.Environment = function.Environment ?? new Dictionary<string, object>();
                    function.VPC = function.VPC ?? new Dictionary<string, object>();
                    ValidateFunctionSource(function.Sources);
                    if(function.Pragmas == null) {
                        function.Pragmas = new List<object>();
                    }
                });
            }
        }

        private void ValidateFunctionSource(IEnumerable<FunctionSourceNode> sources) {
            var index = 0;
            foreach(var source in sources) {
                ++index;
                AtLocation($"{index}", () => {
                    if(source.Api != null) {
                        ValidateNotBothStatements("Api", "Schedule", source.Schedule == null);
                        ValidateNotBothStatements("Api", "Name", source.Name == null);
                        ValidateNotBothStatements("Api", "S3", source.S3 == null);
                        ValidateNotBothStatements("Api", "Events", source.Events == null);
                        ValidateNotBothStatements("Api", "Prefix", source.Prefix == null);
                        ValidateNotBothStatements("Api", "Suffix", source.Suffix == null);
                        ValidateNotBothStatements("Api", "SlackCommand", source.SlackCommand == null);
                        ValidateNotBothStatements("Api", "Topic", source.Topic == null);
                        ValidateNotBothStatements("Api", "Sqs", source.Sqs == null);
                        ValidateNotBothStatements("Api", "BatchSize", source.BatchSize == null);
                        ValidateNotBothStatements("Api", "Alexa", source.Alexa == null);
                        ValidateNotBothStatements("Api", "DynamoDB", source.DynamoDB == null);
                        ValidateNotBothStatements("Api", "StartingPosition", source.StartingPosition == null);
                        ValidateNotBothStatements("Api", "Kinesis", source.Kinesis == null);
                    } else if(source.Schedule != null) {
                        ValidateNotBothStatements("Schedule", "Api", source.Api == null);
                        ValidateNotBothStatements("Schedule", "OperationName", source.OperationName == null);
                        ValidateNotBothStatements("Schedule", "APiKeyRequired", source.ApiKeyRequired == null);
                        ValidateNotBothStatements("Schedule", "Integration", source.Integration == null);
                        ValidateNotBothStatements("Schedule", "S3", source.S3 == null);
                        ValidateNotBothStatements("Schedule", "Events", source.Events == null);
                        ValidateNotBothStatements("Schedule", "Prefix", source.Prefix == null);
                        ValidateNotBothStatements("Schedule", "Suffix", source.Suffix == null);
                        ValidateNotBothStatements("Schedule", "SlackCommand", source.SlackCommand == null);
                        ValidateNotBothStatements("Schedule", "Topic", source.Topic == null);
                        ValidateNotBothStatements("Schedule", "Sqs", source.Sqs == null);
                        ValidateNotBothStatements("Schedule", "BatchSize", source.BatchSize == null);
                        ValidateNotBothStatements("Schedule", "Alexa", source.Alexa == null);
                        ValidateNotBothStatements("Schedule", "DynamoDB", source.DynamoDB == null);
                        ValidateNotBothStatements("Schedule", "StartingPosition", source.StartingPosition == null);
                        ValidateNotBothStatements("Schedule", "Kinesis", source.Kinesis == null);

                        // TODO (2018-06-27, bjorg): add cron/rate expression validation
                    } else if(source.S3 != null) {
                        ValidateNotBothStatements("S3", "Api", source.Api == null);
                        ValidateNotBothStatements("S3", "Integration", source.Integration == null);
                        ValidateNotBothStatements("S3", "OperationName", source.OperationName == null);
                        ValidateNotBothStatements("S3", "APiKeyRequired", source.ApiKeyRequired == null);
                        ValidateNotBothStatements("S3", "Schedule", source.Schedule == null);
                        ValidateNotBothStatements("S3", "Name", source.Name == null);
                        ValidateNotBothStatements("S3", "SlackCommand", source.SlackCommand == null);
                        ValidateNotBothStatements("S3", "Topic", source.Topic == null);
                        ValidateNotBothStatements("S3", "Sqs", source.Sqs == null);
                        ValidateNotBothStatements("S3", "BatchSize", source.BatchSize == null);
                        ValidateNotBothStatements("S3", "Alexa", source.Alexa == null);
                        ValidateNotBothStatements("S3", "DynamoDB", source.DynamoDB == null);
                        ValidateNotBothStatements("S3", "StartingPosition", source.StartingPosition == null);
                        ValidateNotBothStatements("S3", "Kinesis", source.Kinesis == null);

                        // TODO (2018-06-27, bjorg): add events, prefix, suffix validation

                        // verify source exists
                        ValidateSourceParameter(source.S3, "AWS::S3::Bucket", "S3 bucket");
                    } else if(source.SlackCommand != null) {
                        ValidateNotBothStatements("SlackCommand", "Api", source.Api == null);
                        ValidateNotBothStatements("SlackCommand", "Integration", source.Integration == null);
                        ValidateNotBothStatements("SlackCommand", "APiKeyRequired", source.ApiKeyRequired == null);
                        ValidateNotBothStatements("SlackCommand", "Schedule", source.S3 == null);
                        ValidateNotBothStatements("SlackCommand", "Name", source.S3 == null);
                        ValidateNotBothStatements("SlackCommand", "S3", source.S3 == null);
                        ValidateNotBothStatements("SlackCommand", "Events", source.Events == null);
                        ValidateNotBothStatements("SlackCommand", "Prefix", source.Prefix == null);
                        ValidateNotBothStatements("SlackCommand", "Suffix", source.Suffix == null);
                        ValidateNotBothStatements("SlackCommand", "Topic", source.Topic == null);
                        ValidateNotBothStatements("SlackCommand", "Sqs", source.Sqs == null);
                        ValidateNotBothStatements("SlackCommand", "BatchSize", source.BatchSize == null);
                        ValidateNotBothStatements("SlackCommand", "Alexa", source.Alexa == null);
                        ValidateNotBothStatements("SlackCommand", "DynamoDB", source.DynamoDB == null);
                        ValidateNotBothStatements("SlackCommand", "StartingPosition", source.StartingPosition == null);
                        ValidateNotBothStatements("SlackCommand", "Kinesis", source.Kinesis == null);
                    } else if(source.Topic != null) {
                        ValidateNotBothStatements("Topic", "Api", source.Api == null);
                        ValidateNotBothStatements("Topic", "Integration", source.Integration == null);
                        ValidateNotBothStatements("Topic", "OperationName", source.OperationName == null);
                        ValidateNotBothStatements("Topic", "APiKeyRequired", source.ApiKeyRequired == null);
                        ValidateNotBothStatements("Topic", "Schedule", source.S3 == null);
                        ValidateNotBothStatements("Topic", "Name", source.S3 == null);
                        ValidateNotBothStatements("Topic", "S3", source.S3 == null);
                        ValidateNotBothStatements("Topic", "Events", source.Events == null);
                        ValidateNotBothStatements("Topic", "Prefix", source.Prefix == null);
                        ValidateNotBothStatements("Topic", "Suffix", source.Suffix == null);
                        ValidateNotBothStatements("Topic", "SlackCommand", source.SlackCommand == null);
                        ValidateNotBothStatements("Topic", "Sqs", source.Sqs == null);
                        ValidateNotBothStatements("Topic", "BatchSize", source.BatchSize == null);
                        ValidateNotBothStatements("Topic", "Alexa", source.Alexa == null);
                        ValidateNotBothStatements("Topic", "DynamoDB", source.DynamoDB == null);
                        ValidateNotBothStatements("Topic", "StartingPosition", source.StartingPosition == null);
                        ValidateNotBothStatements("Topic", "Kinesis", source.Kinesis == null);

                        // verify source exists
                        ValidateSourceParameter(source.Topic, "AWS::SNS::Topic", "SNS topic");
                    } else if(source.Sqs != null) {
                        ValidateNotBothStatements("Sqs", "Api", source.Api == null);
                        ValidateNotBothStatements("Sqs", "Integration", source.Integration == null);
                        ValidateNotBothStatements("Sqs", "OperationName", source.OperationName == null);
                        ValidateNotBothStatements("Sqs", "APiKeyRequired", source.ApiKeyRequired == null);
                        ValidateNotBothStatements("Sqs", "Schedule", source.S3 == null);
                        ValidateNotBothStatements("Sqs", "Name", source.S3 == null);
                        ValidateNotBothStatements("Sqs", "S3", source.S3 == null);
                        ValidateNotBothStatements("Sqs", "Events", source.Events == null);
                        ValidateNotBothStatements("Sqs", "Prefix", source.Prefix == null);
                        ValidateNotBothStatements("Sqs", "Suffix", source.Suffix == null);
                        ValidateNotBothStatements("Sqs", "SlackCommand", source.SlackCommand == null);
                        ValidateNotBothStatements("Sqs", "Topic", source.Topic == null);
                        ValidateNotBothStatements("Sqs", "Alexa", source.Alexa == null);
                        ValidateNotBothStatements("Sqs", "DynamoDB", source.DynamoDB == null);
                        ValidateNotBothStatements("Sqs", "StartingPosition", source.StartingPosition == null);
                        ValidateNotBothStatements("Sqs", "Kinesis", source.Kinesis == null);

                        // validate settings
                        AtLocation("BatchSize", () => {
                            if((source.BatchSize < 1) || (source.BatchSize > 10)) {
                                AddError($"invalid BatchSize value: {source.BatchSize}");
                            }
                        });

                        // verify source exists
                        ValidateSourceParameter(source.Sqs, "AWS::SQS::Queue", "SQS queue");
                    } else if(source.Alexa != null) {
                        ValidateNotBothStatements("Alexa", "Api", source.Api == null);
                        ValidateNotBothStatements("Alexa", "Integration", source.Integration == null);
                        ValidateNotBothStatements("Alexa", "OperationName", source.OperationName == null);
                        ValidateNotBothStatements("Alexa", "APiKeyRequired", source.ApiKeyRequired == null);
                        ValidateNotBothStatements("Alexa", "Schedule", source.S3 == null);
                        ValidateNotBothStatements("Alexa", "Name", source.S3 == null);
                        ValidateNotBothStatements("Alexa", "S3", source.S3 == null);
                        ValidateNotBothStatements("Alexa", "Events", source.Events == null);
                        ValidateNotBothStatements("Alexa", "Prefix", source.Prefix == null);
                        ValidateNotBothStatements("Alexa", "Suffix", source.Suffix == null);
                        ValidateNotBothStatements("Alexa", "SlackCommand", source.SlackCommand == null);
                        ValidateNotBothStatements("Alexa", "Topic", source.Topic == null);
                        ValidateNotBothStatements("Alexa", "Sqs", source.Sqs == null);
                        ValidateNotBothStatements("Alexa", "BatchSize", source.BatchSize == null);
                        ValidateNotBothStatements("Alexa", "DynamoDB", source.DynamoDB == null);
                        ValidateNotBothStatements("Alexa", "StartingPosition", source.StartingPosition == null);
                        ValidateNotBothStatements("Alexa", "Kinesis", source.Kinesis == null);
                    } else if(source.DynamoDB != null) {
                        ValidateNotBothStatements("DynamoDB", "Api", source.Api == null);
                        ValidateNotBothStatements("DynamoDB", "Integration", source.Integration == null);
                        ValidateNotBothStatements("DynamoDB", "OperationName", source.OperationName == null);
                        ValidateNotBothStatements("DynamoDB", "APiKeyRequired", source.ApiKeyRequired == null);
                        ValidateNotBothStatements("DynamoDB", "Schedule", source.S3 == null);
                        ValidateNotBothStatements("DynamoDB", "Name", source.S3 == null);
                        ValidateNotBothStatements("DynamoDB", "S3", source.S3 == null);
                        ValidateNotBothStatements("DynamoDB", "Events", source.Events == null);
                        ValidateNotBothStatements("DynamoDB", "Prefix", source.Prefix == null);
                        ValidateNotBothStatements("DynamoDB", "Suffix", source.Suffix == null);
                        ValidateNotBothStatements("DynamoDB", "SlackCommand", source.SlackCommand == null);
                        ValidateNotBothStatements("DynamoDB", "Topic", source.Topic == null);
                        ValidateNotBothStatements("DynamoDB", "Sqs", source.Sqs == null);
                        ValidateNotBothStatements("DynamoDB", "Alexa", source.Alexa == null);
                        ValidateNotBothStatements("DynamoDB", "Kinesis", source.Kinesis == null);

                        // validate settings
                        AtLocation("BatchSize", () => {
                            if((source.BatchSize < 1) || (source.BatchSize > 100)) {
                                AddError($"invalid BatchSize value: {source.BatchSize}");
                            }
                        });
                        AtLocation("StartingPosition", () => {
                            switch(source.StartingPosition) {
                            case "TRIM_HORIZON":
                            case "LATEST":
                            case null:
                                break;
                            default:
                                AddError($"invalid StartingPosition value: {source.StartingPosition}");
                                break;
                            }
                        });

                        // verify source exists
                        ValidateSourceParameter(source.DynamoDB, "AWS::DynamoDB::Table", "DynamoDB table");
                    } else if(source.Kinesis != null) {
                        ValidateNotBothStatements("Kinesis", "Api", source.Api == null);
                        ValidateNotBothStatements("Kinesis", "Integration", source.Integration == null);
                        ValidateNotBothStatements("Kinesis", "OperationName", source.OperationName == null);
                        ValidateNotBothStatements("Kinesis", "APiKeyRequired", source.ApiKeyRequired == null);
                        ValidateNotBothStatements("Kinesis", "Schedule", source.S3 == null);
                        ValidateNotBothStatements("Kinesis", "Name", source.S3 == null);
                        ValidateNotBothStatements("Kinesis", "S3", source.S3 == null);
                        ValidateNotBothStatements("Kinesis", "Events", source.Events == null);
                        ValidateNotBothStatements("Kinesis", "Prefix", source.Prefix == null);
                        ValidateNotBothStatements("Kinesis", "Suffix", source.Suffix == null);
                        ValidateNotBothStatements("Kinesis", "SlackCommand", source.SlackCommand == null);
                        ValidateNotBothStatements("Kinesis", "Topic", source.Topic == null);
                        ValidateNotBothStatements("Kinesis", "Sqs", source.Sqs == null);
                        ValidateNotBothStatements("Kinesis", "Alexa", source.Alexa == null);
                        ValidateNotBothStatements("Kinesis", "DynamoDB", source.DynamoDB == null);

                        // validate settings
                        AtLocation("BatchSize", () => {
                            if((source.BatchSize < 1) || (source.BatchSize > 100)) {
                                AddError($"invalid BatchSize value: {source.BatchSize}");
                            }
                        });
                        AtLocation("StartingPosition", () => {
                            switch(source.StartingPosition) {
                            case "TRIM_HORIZON":
                            case "LATEST":
                            case null:
                                break;
                            default:
                                AddError($"invalid StartingPosition value: {source.StartingPosition}");
                                break;
                            }
                        });

                        // verify source exists
                        ValidateSourceParameter(source.Kinesis, "AWS::Kinesis::Stream", "Kinesis stream");
                    } else {
                        AddError("unknown source type");
                    }
                });
            }
        }

        // local functions
        private void ValidateNotBothStatements(string attribute1, string attribute2, bool condition) {
            if(!condition) {
                AddError($"attributes '{attribute1}' and '{attribute2}' are not allowed at the same time");
            }
        }

        private void ValidateSourceParameter(string name, string awsType, string typeDescription) {
            var input = _module.Inputs.FirstOrDefault(i => i.Parameter == name);
            var import = _module.Inputs.FirstOrDefault(i => i.Import == name);
            var parameter = _module.Variables.FirstOrDefault(p => p.Var == name);
            if(input != null) {
                if(input.Resource?.Type != awsType) {
                    AddError($"function source must be an {typeDescription} resource: '{name}'");
                }
            } else if(import != null) {
                if(import.Resource?.Type != awsType) {
                    AddError($"function source must be an {typeDescription} resource: '{name}'");
                }
            } else if(parameter != null) {
                if(parameter.Resource?.Type != awsType) {
                    AddError($"function source must be an {typeDescription} resource: '{name}'");
                }
            } else {
                AddError($"could not find function source: '{name}'");
            }
        }

        private void ValidateInputs(IList<InputNode> inputs) {
            var index = 0;
            foreach(var input in inputs) {
                ++index;
                AtLocation(input.Parameter ?? $"[{index}]", () => {
                    if(input.Type == null) {
                        input.Type = "String";
                    }
                    ValidateScope(input.Scope);
                    if(input.Import != null) {
                        Validate(input.Import.Split("::").Length == 2, "incorrect format for `Import` attribute");
                        ValidateNotBothStatements("Import", "Parameter", input.Parameter == null);
                        ValidateNotBothStatements("Import", "Default", input.Default == null);
                        ValidateNotBothStatements("Import", "ConstraintDescription", input.ConstraintDescription == null);
                        ValidateNotBothStatements("Import", "AllowedPattern", input.AllowedPattern == null);
                        ValidateNotBothStatements("Import", "AllowedValues", input.AllowedValues == null);
                        ValidateNotBothStatements("Import", "MaxLength", input.MaxLength == null);
                        ValidateNotBothStatements("Import", "MaxValue", input.MaxValue == null);
                        ValidateNotBothStatements("Import", "MinLength", input.MinLength == null);
                        ValidateNotBothStatements("Import", "MinValue", input.MinValue == null);
                        ValidateNotBothStatements("Import", "NoEcho", input.NoEcho == null);
                        if(input.Resource != null) {
                            Validate(input.Type == "String", "input 'Type' must be string");
                            AtLocation("Resource", () => {
                                Validate(input.Resource.Type != null, "'Type' attribute is required");
                                Validate(input.Resource.Allow != null, "'Allow' attribute is required");
                                ValidateNotBothStatements("Import", "Properties", input.Resource.Properties == null);
                                Validate(ConvertToStringList(input.Resource.DependsOn).Any() != true, "'DependsOn' cannot be used on an input");
                            });
                        }
                    } else {
                        ValidateResourceName(input.Parameter, "");
                        if(input.Resource != null) {
                            Validate(input.Type == "String", "input 'Type' must be string");
                            AtLocation("Resource", () => {
                                Validate(ConvertToStringList(input.Resource.DependsOn).Any() != true, "'DependsOn' cannot be used on an input");
                                if(input.Default == null) {
                                    Validate(input.Resource.Properties == null, "'Properties' section cannot be used with `Input` attribute unless the 'Default' is set to a blank string");
                                }
                            });
                        }
                    }
                });
            }
        }

        private void ValidateOutputs(IList<OutputNode> outputs) {
            var index = 0;
            foreach(var output in outputs) {
                ++index;
                AtLocation(output.Export ?? output.CustomResource ?? $"[{index}]", () => {
                    if(output.Export != null) {

                        // TODO (2018-09-20, bjorg): add name validation
                        if(
                            (output.Value == null)
                            && (_module.Variables.FirstOrDefault(p => p?.Var == output.Export) == null)
                            && (_module.Inputs.FirstOrDefault(i => i?.Parameter == output.Export) == null)
                        ) {
                            AddError("output must either have a Value attribute or match the name of an existing variable/parameter");
                        }
                        ValidateNotBothStatements("Output", "CustomResource", output.CustomResource == null);
                        ValidateNotBothStatements("Output", "Handler", output.Handler == null);
                        ValidateNotBothStatements("Output", "Macro", output.Macro == null);
                    } else if(output.CustomResource != null) {

                        // TODO (2018-09-20, bjorg): add custom resource name validation

                        Validate(output.Handler != null, "missing Handler attribute");

                        // TODO (2018-09-20, bjorg): confirm that `Handler` is set to an SNS topic or lambda function

                        ValidateNotBothStatements("CustomResource", "Output", output.Export == null);
                        ValidateNotBothStatements("CustomResource", "Value", output.Value == null);
                        ValidateNotBothStatements("CustomResource", "Macro", output.Macro == null);
                    } else if(output.Macro != null) {
                        ValidateNotBothStatements("Macro", "Output", output.Export == null);
                        ValidateNotBothStatements("Macro", "CustomResource", output.CustomResource == null);

                        // TODO (2018-10-30, bjorg): confirm that `Handler` is set to a lambda function

                    } else {
                        AddError("unknown output type");
                    }
                });
            }
        }

        private void ValidateResourceName(string name, string prefix) {
            var fullname = prefix + name;
            if(name == null) {
                AddError("missing name");
            } else if(fullname == "Module") {
                AddError($"'{fullname}' is a reserved name");
            } else if(!_names.Add(fullname)) {
                AddError($"duplicate name '{fullname}'");
            } else {
                Validate(Regex.IsMatch(name, CLOUDFORMATION_ID_PATTERN), "name is not valid");
            }
        }

        private void ValidateScope(object scope) {
            AtLocation("Scope", () => {
                if(scope == null) {
                    return;
                }
                var names = new List<string>();
                if(scope is string text) {
                    names.AddRange(text.Split(",").Select(v => v.Trim()).Where(v => v.Length > 0));
                }
                if(scope is IList<object> list) {
                    foreach(var entry in list) {
                        if(entry is string value) {
                            names.AddRange(value.Split(",").Select(v => v.Trim()).Where(v => v.Length > 0));
                        } else {
                            AddError("invalid function name");
                        }
                    }
                }
                foreach(var name in names) {
                    ValidateFunctionName(name);
                }
            });

            // local function
            void ValidateFunctionName(string function) {
                if(function == "*") {
                    return;
                }
                if(!_module.Functions.Any(f => f.Function == function)) {
                    AddError($"could not find function named: {function}");
                }
            }
        }
    }
}