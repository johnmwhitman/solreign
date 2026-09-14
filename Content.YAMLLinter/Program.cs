using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Content.IntegrationTests;
using Content.IntegrationTests.Utility;
using Robust.Shared.Prototypes;
using Robust.Shared.Reflection;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Robust.UnitTesting;
using Robust.UnitTesting.Pool;

namespace Content.YAMLLinter
{
    internal static class Program
    {
        private static readonly ExternalTestContext TestContext = new("YAML Linter", StreamWriter.Null);

        /// <summary>
        /// The integration test assembly, whose static prototype-id fields are out of the linter's
        /// jurisdiction. It is only in <see cref="IReflectionManager.FindAllTypes"/> at all because
        /// this project references it for <see cref="PoolManager"/>, and validating it produces
        /// false positives in both passes: the client instance cannot resolve prototype kinds that
        /// live in Content.Server, and neither instance indexes ids declared inline via
        /// <c>[TestPrototypes]</c>, since the linter only reads on-disk Resources/. Those ids are
        /// real at test runtime, so the test run is what validates them.
        /// </summary>
        private static readonly Assembly TestAssembly = typeof(GameDataScrounger).Assembly;

        private static async Task<int> Main(string[] _)
        {
            // If the anchor type ever moved into a shipping assembly, that whole assembly would go
            // unvalidated with no visible sign. Fail loudly instead of silently linting less.
            if (TestAssembly.GetName().Name != "Content.IntegrationTests")
            {
                Console.WriteLine(
                    $"::error Static-field skip anchor resolved to '{TestAssembly.GetName().Name}', "
                    + "not Content.IntegrationTests. Refusing to run with an unknown skip target.");
                return -1;
            }

            GameDataScrounger.NoScrounging = true; // Ugly hack for YAML Linter.
            PoolManager.Startup();
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            var (errors, fieldErrors) = await RunValidation();

            var count = errors.Count + fieldErrors.Count;

            if (count == 0)
            {
                Console.WriteLine($"No errors found in {(int) stopwatch.Elapsed.TotalMilliseconds} ms.");
                PoolManager.Shutdown();
                return 0;
            }

            foreach (var (file, errorHashset) in errors)
            {
                foreach (var errorNode in errorHashset)
                {
                    // TODO YAML LINTER Fix inheritance
                    // If a parent/abstract prototype has na error, this will misreport the file name (but with the correct line/column).
                    Console.WriteLine($"::error in {file}({errorNode.Node.Start.Line},{errorNode.Node.Start.Column})  {errorNode.ErrorReason}");
                }
            }

            foreach (var error in fieldErrors)
            {
                Console.WriteLine(error);
            }

            Console.WriteLine($"{count} errors found in {(int) stopwatch.Elapsed.TotalMilliseconds} ms.");
            PoolManager.Shutdown();
            return -1;
        }

        private static async Task<(Dictionary<string, HashSet<ErrorNode>> YamlErrors, List<string> FieldErrors)>
            ValidateClient()
        {
            await using var pair = await PoolManager.GetServerClient(testContext: TestContext);
            var client = pair.Client;
            var result = await ValidateInstance(client);
            await pair.CleanReturnAsync();
            return result;
        }

        private static async Task<(Dictionary<string, HashSet<ErrorNode>> YamlErrors, List<string> FieldErrors)>
            ValidateServer()
        {
            await using var pair = await PoolManager.GetServerClient(testContext: TestContext);
            var server = pair.Server;
            var result = await ValidateInstance(server);
            await pair.CleanReturnAsync();
            return result;
        }

        private static async Task<(Dictionary<string, HashSet<ErrorNode>>, List<string>)> ValidateInstance(
            RobustIntegrationTest.IntegrationInstance instance)
        {
            var protoMan = instance.ResolveDependency<IPrototypeManager>();
            var reflection = instance.ResolveDependency<IReflectionManager>();
            Dictionary<string, HashSet<ErrorNode>> yamlErrors = default!;
            List<string> fieldErrors = default!;

            await instance.WaitPost(() =>
            {
                var engineErrors = protoMan.ValidateDirectory(new ResPath("/EnginePrototypes"), out var engPrototypes);
                yamlErrors = protoMan.ValidateDirectory(new ResPath("/Prototypes"), out var prototypes);

                // Merge engine & content prototypes
                foreach (var (kind, instances) in engPrototypes)
                {
                    if (prototypes.TryGetValue(kind, out var existing))
                        existing.UnionWith(instances);
                    else
                        prototypes[kind] = instances;
                }

                foreach (var (kind, set) in engineErrors)
                {
                    if (yamlErrors.TryGetValue(kind, out var existing))
                        existing.UnionWith(set);
                    else
                        yamlErrors[kind] = set;
                }

                fieldErrors = ValidateShippedStaticFields(protoMan, reflection, prototypes);
            });

            return (yamlErrors, fieldErrors);
        }

        /// <summary>
        /// Equivalent to <see cref="IPrototypeManager.ValidateStaticFields(Dictionary{Type, HashSet{string}})"/>,
        /// except it skips <see cref="TestAssembly"/>. Same abstract-type rule as the engine walk.
        /// </summary>
        private static List<string> ValidateShippedStaticFields(
            IPrototypeManager protoMan,
            IReflectionManager reflection,
            Dictionary<Type, HashSet<string>> prototypes)
        {
            var errors = new List<string>();

            foreach (var type in reflection.FindAllTypes())
            {
                if (type.IsAbstract || type.Assembly == TestAssembly)
                    continue;

                errors.AddRange(protoMan.ValidateStaticFields(type, prototypes));
            }

            return errors;
        }

        public static async Task<(Dictionary<string, HashSet<ErrorNode>> YamlErrors, List<string> FieldErrors)>
            RunValidation()
        {
            var (clientAssemblies, serverAssemblies) = await GetClientServerAssemblies();
            var serverTypes = serverAssemblies.SelectMany(n => n.GetTypes()).Select(t => t.Name).ToHashSet();
            var clientTypes = clientAssemblies.SelectMany(n => n.GetTypes()).Select(t => t.Name).ToHashSet();

            var yamlErrors = new Dictionary<string, HashSet<ErrorNode>>();

            var serverErrors = await ValidateServer();
            var clientErrors = await ValidateClient();

            foreach (var (key, val) in serverErrors.YamlErrors)
            {
                // Include all server errors marked as always relevant
                var newErrors = val.Where(n => n.AlwaysRelevant).ToHashSet();

                // We include sometimes-relevant errors if they exist both for the client & server
                if (clientErrors.YamlErrors.TryGetValue(key, out var clientVal))
                    newErrors.UnionWith(val.Intersect(clientVal));

                // Include any errors that relate to server-only types
                foreach (var errorNode in val)
                {
                    if (errorNode is FieldNotFoundErrorNode fieldNotFoundNode && !clientTypes.Contains(fieldNotFoundNode.FieldType.Name))
                    {
                        newErrors.Add(errorNode);
                    }
                }

                if (newErrors.Count != 0)
                    yamlErrors[key] = newErrors;
            }

            // Next add any always-relevant client errors.
            foreach (var (key, val) in clientErrors.YamlErrors)
            {
                var newErrors = val.Where(n => n.AlwaysRelevant).ToHashSet();

                // Include any errors that relate to client-only types
                foreach (var errorNode in val)
                {
                    if (errorNode is FieldNotFoundErrorNode fieldNotFoundNode
                        && !serverTypes.Contains(fieldNotFoundNode.FieldType.Name))
                    {
                        newErrors.Add(errorNode);
                    }
                }

                if (newErrors.Count == 0)
                    continue;

                if (yamlErrors.TryGetValue(key, out var errors))
                    errors.UnionWith(newErrors);
                else
                    yamlErrors[key] = newErrors;
            }

            // Finally, combine the prototype ID field errors.
            var fieldErrors = serverErrors.FieldErrors
                .Concat(clientErrors.FieldErrors)
                .Distinct()
                .ToList();

            return (yamlErrors, fieldErrors);
        }

        private static async Task<(Assembly[] clientAssemblies, Assembly[] serverAssemblies)>
            GetClientServerAssemblies()
        {
            await using var pair = await PoolManager.GetServerClient(testContext: TestContext);

            var result = (GetAssemblies(pair.Client), GetAssemblies(pair.Server));

            await pair.CleanReturnAsync();

            return result;

            Assembly[] GetAssemblies(RobustIntegrationTest.IntegrationInstance instance)
            {
                var refl = instance.ResolveDependency<IReflectionManager>();
                return refl.Assemblies.ToArray();
            }
        }
    }
}
