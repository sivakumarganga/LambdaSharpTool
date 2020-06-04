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

#nullable disable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LambdaSharp.Compiler;
using LambdaSharp.Compiler.Model;
using LambdaSharp.Tool.Internal;

namespace LambdaSharp.Tool.Model {
    using static ModelFunctions;

    public class ModuleBuilderDependency {

        //--- Properties ---
        public ModuleManifest Manifest { get; set; }
        public ModuleLocation ModuleLocation { get; set; }
        public ModuleManifestDependencyType Type;
    }

    public class ModuleBuilder : AModelProcessor {

        //--- Fields ---
        private string _namespace;
        private string _name;
        private string _description;
        private IList<object> _pragmas;
        private IList<object> _secrets;
        private Dictionary<string, AModuleItem> _itemsByFullName;
        private List<AModuleItem> _items;
        private IList<Humidifier.Statement> _resourceStatements = new List<Humidifier.Statement>();
        private IList<string> _artifacts;
        private IDictionary<string, ModuleBuilderDependency> _dependencies;
        private IList<ModuleManifestResourceType> _customResourceTypes;
        private IList<string> _macroNames;
        private IDictionary<string, string> _resourceTypeNameMappings;

        //--- Constructors ---
        public ModuleBuilder(Settings settings, string sourceFilename, Module module) : base(settings, sourceFilename) {
            _namespace = module.Namespace;
            _name = module.Name;
            Version = module.Version;
            _description = module.Description;
            _pragmas = new List<object>(module.Pragmas ?? new object[0]);
            _secrets = new List<object>(module.Secrets ?? new object[0]);
            _items = new List<AModuleItem>(module.Items ?? new AModuleItem[0]);
            _itemsByFullName = _items.ToDictionary(item => item.FullName);
            _artifacts = new List<string>(module.Artifacts ?? new string[0]);
            _dependencies = (module.Dependencies != null)
                ? new Dictionary<string, ModuleBuilderDependency>(module.Dependencies)
                : new Dictionary<string, ModuleBuilderDependency>();
            _customResourceTypes = (module.CustomResourceTypes != null)
                ? new List<ModuleManifestResourceType>(module.CustomResourceTypes)
                : new List<ModuleManifestResourceType>();
            _macroNames = new List<string>(module.MacroNames ?? new string[0]);
            _resourceTypeNameMappings = module.ResourceTypeNameMappings ?? new Dictionary<string, string>();

            // extract existing resource statements when they exist
            if(TryGetItem("Module::Role", out var moduleRoleItem)) {
                var role = (Humidifier.IAM.Role)((ResourceItem)moduleRoleItem).Resource;
                _resourceStatements = new List<Humidifier.Statement>(role.Policies[0].PolicyDocument.Statement);
                role.Policies[0].PolicyDocument.Statement = new List<Humidifier.Statement>();
            } else {
                _resourceStatements = new List<Humidifier.Statement>();
            }
        }

        //--- Properties ---
        public string FullName => $"{_namespace}.{_name}";
        public VersionInfo Version { get; set; }
        public IEnumerable<AModuleItem> Items => _items;

        public bool TryGetLabeledPragma(string key, out object value) {
            foreach(var dictionaryPragma in _pragmas.OfType<IDictionary>()) {
                var entry = dictionaryPragma[key];
                if(entry != null) {
                    value = entry;
                    return true;
                }
            }
            value = null;
            return false;
        }

        public bool TryGetOverride(string key, out object expression) {
            if(
                TryGetLabeledPragma("Overrides", out var value)
                && (value is IDictionary dictionary)
            ) {
                var entry = dictionary[key];
                if(entry != null) {
                    expression = entry;
                    return true;
                }
            }
            expression = null;
            return false;
        }

        //--- Methods ---
        public AModuleItem GetItem(string fullNameOrResourceName) {
            if(fullNameOrResourceName.StartsWith("@", StringComparison.Ordinal)) {
                return _items.FirstOrDefault(e => e.ResourceName == fullNameOrResourceName) ?? throw new KeyNotFoundException(fullNameOrResourceName);
            }
            return _itemsByFullName[fullNameOrResourceName];
        }

        public bool TryGetItem(string fullNameOrResourceName, out AModuleItem item) {
            if(fullNameOrResourceName == null) {
                item = null;
                return false;
            }
            if(fullNameOrResourceName.StartsWith("@", StringComparison.Ordinal)) {
                item = _items.FirstOrDefault(e => e.ResourceName == fullNameOrResourceName);
                return item != null;
            }
            return _itemsByFullName.TryGetValue(fullNameOrResourceName, out item);
        }

        public void AddArtifact(string fullName, string artifact) {
            _artifacts.Add(Path.GetRelativePath(Settings.OutputDirectory, artifact));

            // update item with the name of the artifact
            GetItem(fullName).Reference = Path.GetFileName(artifact);
        }

        public async Task<ModuleBuilderDependency> AddDependencyAsync(ModuleInfo moduleInfo, ModuleManifestDependencyType dependencyType) {
            string moduleKey;
            switch(dependencyType) {
            case ModuleManifestDependencyType.Nested:

                // nested dependencies can reference different versions
                moduleKey = moduleInfo.ToString();
                if(_dependencies.ContainsKey(moduleKey)) {
                    return null;
                }
                break;
            case ModuleManifestDependencyType.Shared:

                // shared dependencies can only have one version
                moduleKey = moduleInfo.WithoutVersion().ToString();

                // check if a dependency was already registered
                if(_dependencies.TryGetValue(moduleKey, out var existingDependency)) {
                    if(
                        (moduleInfo.Version == null)
                        || (
                            (existingDependency.ModuleLocation.ModuleInfo.Version != null)
                            && existingDependency.ModuleLocation.ModuleInfo.Version.IsGreaterOrEqualThanVersion(moduleInfo.Version)
                        )
                    ) {

                        // keep existing shared dependency
                        return null;
                    }
                }
                break;
            default:
                LogError($"unsupported depency type '{dependencyType}' for {moduleInfo.ToString()}");
                return null;
            }

            // validate dependency
            var loader = new ModelManifestLoader(Settings, SourceFilename);
            ModuleBuilderDependency dependency;
            if(!Settings.NoDependencyValidation) {
                dependency = new ModuleBuilderDependency {
                    Type = dependencyType,
                    ModuleLocation = await loader.ResolveInfoToLocationAsync(moduleInfo, dependencyType, allowImport: true, showError: true, allowCaching: true)
                };
                if(dependency.ModuleLocation == null) {

                    // nothing to do; loader already emitted an error
                    return null;
                }
                dependency.Manifest = await loader.LoadManifestFromLocationAsync(dependency.ModuleLocation, allowCaching: true);
                if(dependency.Manifest == null) {

                    // nothing to do; loader already emitted an error
                    return null;
                }
            } else {
                LogWarn("unable to validate dependency");
                dependency = new ModuleBuilderDependency {
                    Type = dependencyType,
                    ModuleLocation = new ModuleLocation(Settings.DeploymentBucketName, moduleInfo, "00000000000000000000000000000000")
                };
            }
            _dependencies[moduleKey] = dependency;
            return dependency;
        }

        public AModuleItem AddImport(
            AModuleItem parent,
            string name,
            string description,
            string type,
            IList<string> scope,
            object allow,
            string module,
            IDictionary<string, string> encryptionContext,
            out string parameterName
        ) {

            // extract optional export name from module reference
            var export = name;
            var moduleParts = module.Split("::", 2);
            if(moduleParts.Length == 2) {
                module = moduleParts[0];
                export = moduleParts[1];
            }

            // validate module name
            if(!ModuleInfo.TryParse(module, out var moduleInfo)) {
                LogError("invalid 'Module' attribute");
            } else {
                module = moduleInfo.FullName;
            }
            if(moduleInfo.Version != null) {
                LogError("'Module' attribute cannot have a version");
            }
            if(moduleInfo.Origin != null) {
                LogError("'Module' attribute cannot have an origin");
            }

            // create input parameter item
            var parameter = new Humidifier.Parameter {
                Type = ResourceMapping.ToCloudFormationParameterType(type),
                Description = $"Cross-module reference for {module}::{export}",

                // default value for an imported parameter is always the cross-module reference
                Default = $"${module.Replace(".", "-")}::{export}",

                // set default settings for import parameters
                ConstraintDescription = "must either be a cross-module reference or a non-empty value",
                AllowedPattern =  @"^.+$"
            };
            var import = new ParameterItem(
                parent: null,
                name: module.ToIdentifier() + export.ToIdentifier(),
                section: $"{module} Imports",
                label: export,
                description: null,
                type: type,
                scope: null,
                reference: null,
                parameter: parameter,
                import: $"{module}::{export}"
            );
            parameterName = import.ResourceName;
            import.DiscardIfNotReachable = true;

            // check if an import parameter for this reference exists already
            var found = _items.FirstOrDefault(item => item.FullName == import.FullName);
            if(found is ParameterItem existing) {
                if(existing.Parameter.Default != parameter.Default) {
                    LogError($"import parameter '{import.FullName}' is already defined with a different binding");
                }
                import = existing;
            } else {

                // add parameter and map it to variable
                AddItem(import);
                var condition = AddCondition(
                    parent: import,
                    name: "IsImported",
                    description: null,
                    value: FnAnd(
                        FnNot(FnEquals(FnRef(import.ResourceName), "")),
                        FnEquals(FnSelect("0", FnSplit("$", FnRef(import.ResourceName))), "")
                    )
                );

                // check if import itself is conditional
                import.Reference = FnIf(
                    condition.ResourceName,
                    FnImportValue(FnSub("${DeploymentPrefix}${Import}", new Dictionary<string, object> {
                        ["Import"] = FnSelect("1", FnSplit("$", FnRef(import.ResourceName)))
                    })),
                    FnRef(import.ResourceName)
                );
            }

            // TODO (2019-02-07, bjorg): since the variable is created for each import, it also duplicates the '::Plaintext' sub-resource
            //  for imports of type 'Secret'; while it's technically not wrong, it's not efficient when multiple secrets are being imported.

            // register import parameter reference
            return AddVariable(
                parent: parent,
                name: name,
                description: description,
                type: type,
                scope: scope,
                value: FnRef(import.FullName),
                allow: allow,
                encryptionContext: encryptionContext
            );
        }

        public AModuleItem AddVariable(
            AModuleItem parent,
            string name,
            string description,
            string type,
            IList<string> scope,
            object value,
            object allow,
            IDictionary<string, string> encryptionContext
        ) {
            if(value == null) {
                throw new ArgumentNullException(nameof(value));
            }
            var result = AddItem(new VariableItem(parent, name, description, type, scope, reference: null));

            // the format for secrets with encryption keys is: SECRET|KEY1=VALUE1|KEY2=VALUE2
            if(encryptionContext != null) {
                Validate(type == "Secret", "type must be 'Secret' to use 'EncryptionContext'");
                result.Reference = FnJoin(
                    "|",
                    new object[] {
                        value
                    }.Union(
                        encryptionContext.Select(kv => $"{kv.Key}={kv.Value}")
                    ).ToArray()
                );
            } else {
                result.Reference = (value is IList<object> values)
                    ? FnJoin(",", values)
                    : value;
            }

            // check if value must be decrypted
            if(result.HasSecretType) {
                var decoder = AddResource(
                    parent: result,
                    name: "Plaintext",
                    description: null,
                    scope: null,
                    resource: CreateDecryptSecretResourceFor(result),
                    resourceExportAttribute: null,
                    dependsOn: null,
                    condition: null,
                    pragmas: null
                );
                decoder.Reference = FnGetAtt(decoder.ResourceName, "Plaintext");
                decoder.DiscardIfNotReachable = true;
            }

            // add optional grants
            if(allow != null) {
                AddGrant(result.LogicalId, type, value, allow, condition: null);
            }
            return result;
        }

        public AModuleItem AddResource(
            AModuleItem parent,
            string name,
            string description,
            IList<string> scope,
            Humidifier.Resource resource,
            string resourceExportAttribute,
            IList<string> dependsOn,
            object condition,
            IList<object> pragmas
        ) {

            // set a default export attribute if none is provided
            if(resourceExportAttribute == null) {
                var resourceTypeName = (resource is Humidifier.CustomResource customResource)
                    ? customResource.OriginalTypeName
                    : resource.AWSTypeName;
                if(ResourceMapping.HasAttribute(resourceTypeName, "Arn")) {

                    // for built-in type, use the 'Arn' attribute if it exists
                    resourceExportAttribute = "Arn";
                } else if(TryGetResourceType(resourceTypeName, out var resourceType)) {

                    // for custom resource types, use the first defined response attribute
                    resourceExportAttribute = resourceType.Attributes.FirstOrDefault()?.Name;
                }
            }

            // create resource
            var result = new ResourceItem(
                parent: parent,
                name: name,
                description: description,
                scope: scope,
                resource: resource,
                resourceExportAttribute: resourceExportAttribute,
                dependsOn: dependsOn,
                condition: null,
                pragmas: pragmas
            );
            AddItem(result);

            // add condition
            if(condition is string conditionName) {
                result.Condition = conditionName;
            } else if(condition != null) {
                var conditionItem = AddCondition(
                    parent: result,
                    name: "Condition",
                    description: null,
                    value: condition
                );
                result.Condition = conditionItem.FullName;
            }
            return result;
        }

        public AModuleItem AddResource(
            AModuleItem parent,
            string name,
            string description,
            string type,
            IList<string> scope,
            object allow,
            IDictionary<string, object> properties,
            IList<string> dependsOn,
            string arnAttribute,
            object condition,
            IList<object> pragmas
        ) {

            // create resource item
            var customResource = RegisterCustomResourceNameMapping(new Humidifier.CustomResource(type, properties));

            // add resource
            var result = AddResource(
                parent: parent,
                name: name,
                description: description,
                scope: scope,
                resource: customResource,
                resourceExportAttribute: arnAttribute,
                dependsOn: dependsOn,
                condition: condition,
                pragmas: pragmas
            );

            // validate resource properties
            if(result.HasTypeValidation) {
                // ValidateProperties(type, customResource);
            }

            // add optional grants
            if(allow != null) {
                AddGrant(result.LogicalId, type, result.GetExportReference(), allow, condition: null);
            }
            return result;
        }

        public AModuleItem AddNestedModule(
            AModuleItem parent,
            string name,
            string description,
            ModuleInfo moduleInfo,
            IList<string> scope,
            object dependsOn,
            IDictionary<string, object> parameters
        ) {
            var moduleParameters = (parameters != null)
                ? new Dictionary<string, object>(parameters)
                : new Dictionary<string, object>();
            if(moduleInfo.Version == null) {
                LogError("missing module version");
            }

            // add nested module resource
            var stack = new Humidifier.CloudFormation.Stack {
                NotificationARNs = FnRef("AWS::NotificationARNs"),
                Parameters = moduleParameters,
                Tags = new List<Humidifier.Tag> {
                    new Humidifier.Tag {
                        Key = "LambdaSharp:Module",
                        Value = moduleInfo.FullName
                    }
                },

                // this value gets set once the template was successfully loaded for validation
                TemplateURL = "<BAD>",

                // TODO (2018-11-29, bjorg): make timeout configurable
                TimeoutInMinutes = 15
            };
            var resource = AddResource(
                parent: parent,
                name: name,
                description: description,
                scope: scope,
                resource: stack,
                resourceExportAttribute: null,
                dependsOn: ConvertToStringList(dependsOn),
                condition: null,
                pragmas: null
            );
            var dependency = AddDependencyAsync(moduleInfo, ModuleManifestDependencyType.Nested).Result;

            // validate module parameters
            AtLocation("Parameters", () => {
                if(dependency?.Manifest != null) {
                    if(!Settings.NoDependencyValidation) {
                        var manifest = dependency.Manifest;

                        // update stack resource source with hashed cloudformation key
                        stack.TemplateURL = FnSub($"https://${{DeploymentBucketName}}.s3.amazonaws.com/{dependency.ModuleLocation.ModuleTemplateKey}");

                        // validate that all required parameters are supplied
                        var formalParameters = manifest.GetAllParameters().ToDictionary(p => p.Name);
                        foreach(var formalParameter in formalParameters.Values.Where(p => (p.Default == null) && !moduleParameters.ContainsKey(p.Name))) {
                            LogError($"missing module parameter '{formalParameter.Name}'");
                        }

                        // validate that all supplied parameters exist
                        foreach(var moduleParameter in moduleParameters.Where(kv => !formalParameters.ContainsKey(kv.Key))) {
                            LogError($"unknown module parameter '{moduleParameter.Key}'");
                        }

                        // inherit dependencies from nested module
                        foreach(var manifestDependency in manifest.Dependencies) {
                            AddDependencyAsync(manifestDependency.ModuleInfo, manifestDependency.Type).Wait();
                        }

                        // inherit import parameters that are not provided by the declaration
                        foreach(var nestedImport in manifest.GetAllParameters()
                            .Where(parameter => parameter.Import != null)
                            .Where(parameter => !moduleParameters.ContainsKey(parameter.Name))
                        ) {
                            var import = AddImport(
                                parent: resource,
                                name: nestedImport.Name,
                                description: null,
                                type: nestedImport.Type,
                                scope: null,
                                allow: null,
                                module: nestedImport.Import,
                                encryptionContext: null,
                                out var parameterName
                            );
                            moduleParameters.Add(nestedImport.Name, FnRef(parameterName));
                        }

                        // check if x-ray tracing should be enabled in nested module
                        if(formalParameters.ContainsKey("XRayTracing") && !moduleParameters.ContainsKey("XRayTracing")) {
                            moduleParameters.Add("XRayTracing", FnIf("XRayNestedIsEnabled", XRayTracingLevel.AllModules.ToString(), XRayTracingLevel.Disabled.ToString()));
                        }
                    } else {
                        LogWarn("unable to validate nested module parameters");
                    }
                } else {

                    // nothing to do; loader already emitted an error
                }

                // add expected parameters
                MandatoryAdd("DeploymentBucketName", FnRef("DeploymentBucketName"));
                MandatoryAdd("DeploymentPrefix", FnRef("DeploymentPrefix"));
                MandatoryAdd("DeploymentPrefixLowercase", FnRef("DeploymentPrefixLowercase"));
                MandatoryAdd("DeploymentRoot", FnRef("Module::RootId"));
                MandatoryAdd("LambdaSharpCoreServices", FnRef("LambdaSharpCoreServices"));
            });
            return resource;

            // local function
            void MandatoryAdd(string key, object value) {
                if(!moduleParameters.ContainsKey(key)) {
                    moduleParameters.Add(key, value);
                } else {
                    LogError($"'{key}' is a reserved attribute and cannot be specified");
                }
            }
        }

        public AModuleItem AddCondition(
            AModuleItem parent,
            string name,
            string description,
            object value
        ) {
            return AddItem(new ConditionItem(
                parent: parent,
                name: name,
                description: description,
                value: value
            ));
        }

        public void AddGrant(string name, string awsType, object reference, object allow, object condition) {

            // resolve shorthands and deduplicate statements
            var allowStatements = new List<string>();
            foreach(var allowStatement in ConvertToStringList(allow)) {
                if(allowStatement == "None") {

                    // nothing to do
                } else if(allowStatement.Contains(':')) {

                    // AWS permission statements always contain a ':' (e.g 'ssm:GetParameter')
                    allowStatements.Add(allowStatement);
                } else if((awsType != null) && ResourceMapping.TryResolveAllowShorthand(awsType, allowStatement, out var allowedList)) {
                    allowStatements.AddRange(allowedList);
                } else {
                    LogError($"could not find IAM mapping for short-hand '{allowStatement}' on AWS type '{awsType ?? "<omitted>"}'");
                }
            }
            if(!allowStatements.Any()) {
                return;
            }

            // check if statement can be added to role directly or needs to be attached as a conditional policy resource
            if(condition != null) {
                AddResource(
                    parent: GetItem("Module::Role"),
                    name: name + "Policy",
                    description: null,

                    // by scoping this resource to all Lambda functions, we ensure the policy is created before a Lambda executes
                    scope: new[] { "all" },

                    resource: new Humidifier.IAM.Policy {
                        PolicyName = FnSub($"${{AWS::StackName}}ModuleRole{name}"),
                        PolicyDocument = new Humidifier.PolicyDocument {
                            Version = "2012-10-17",
                            Statement = new List<Humidifier.Statement> {
                                new Humidifier.Statement {
                                    Sid = name.ToIdentifier(),
                                    Effect = "Allow",
                                    Resource = ResourceMapping.ExpandResourceReference(awsType, reference),
                                    Action = allowStatements.Distinct().OrderBy(text => text).ToList()
                                }
                            }
                        },
                        Roles = new List<object> {
                            FnRef("Module::Role")
                        }
                    },
                    resourceExportAttribute: null,
                    dependsOn: null,
                    condition: condition,
                    pragmas: null
                ).DiscardIfNotReachable = true;
            } else {

                // add role resource statement
                var statement = new Humidifier.Statement {
                    Sid = name.ToIdentifier(),
                    Effect = "Allow",
                    Resource = ResourceMapping.ExpandResourceReference(awsType, reference),
                    Action = allowStatements.Distinct().OrderBy(text => text).ToList()
                };

                // check if an existing statement is being updated
                for(var i = 0; i < _resourceStatements.Count; ++i) {
                    if(_resourceStatements[i].Sid == name) {
                        _resourceStatements[i] = statement;
                        return;
                    }
                }

                // add new statement
                _resourceStatements.Add(statement);
            }
        }

        private AModuleItem AddItem(AModuleItem item) {
            Validate(Regex.IsMatch(item.Name, CLOUDFORMATION_ID_PATTERN), "name is not valid");

            // set default reference
            if(item.Reference == null) {
                item.Reference = FnRef(item.ResourceName);
            }

            // add item
            if(_itemsByFullName.TryAdd(item.FullName, item)) {
                _items.Add(item);
            } else {
                LogError($"duplicate name '{item.FullName}'");
            }
            return item;
        }

        private Humidifier.CustomResource CreateDecryptSecretResourceFor(AModuleItem item)
            => RegisterCustomResourceNameMapping(new Humidifier.CustomResource("Module::DecryptSecret") {
                ["ServiceToken"] = FnGetAtt("Module::DecryptSecretFunction", "Arn"),
                ["Ciphertext"] = FnRef(item.FullName)
            });

        private Humidifier.CustomResource RegisterCustomResourceNameMapping(Humidifier.CustomResource customResource) {
            if(customResource.AWSTypeName != customResource.OriginalTypeName) {
                _resourceTypeNameMappings[customResource.AWSTypeName] = customResource.OriginalTypeName;
            }
            return customResource;
        }

        private bool TryGetResourceType(string resourceTypeName, out ModuleManifestResourceType resourceType) {
            var matches = _dependencies
                .Where(kv => kv.Value.Type == ModuleManifestDependencyType.Shared)
                .Select(kv => new {
                    Found = kv.Value.Manifest?.ResourceTypes.FirstOrDefault(existing => existing.Type == resourceTypeName),
                    From = kv.Key
                })
                .Where(foundResourceType => foundResourceType.Found != null)
                .ToArray();
            switch(matches.Length) {
            case 0:
                resourceType = null;
                return false;
            case 1:
                resourceType = matches[0].Found;
                return true;
            default:
                LogWarn($"ambiguous resource type '{resourceTypeName}' [{string.Join(", ", matches.Select(t => t.From))}]");
                resourceType = matches[0].Found;
                return true;
            }
        }
    }
}