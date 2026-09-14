#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using NUnit.Framework;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     Phase-2 Track C3 (docs/plans/2026-07-11-ROADMAP-PHASE2.md §2, Track C, Wave C3): permanent
///     boot-time integrity gate for the "werewolf lesson" defect class (retro §1 L9 / Track A2's
///     silent-failure sweep, docs/specs/2026-07-11-orphan-report.md) — a C# field typed
///     <see cref="EntProtoId"/>, <see cref="EntProtoId{T}"/>, or <see cref="ProtoId{T}"/> carrying
///     a compile-time string default that names a prototype nobody ever wrote.
///
///     Why the engine's own prototype validation does NOT already catch this: <see cref="ProtoId{T}"/>
///     and <see cref="EntProtoId"/> are "automatically validated ... if used in data fields" (see
///     their own doc comments) — but that validation runs on values the YAML deserializer actually
///     reads off an entity prototype. A C#-side field default (e.g. the exact regression this test
///     guards, <c>SolreignWerewolfComponent.WolfPolymorphPrototype = "SolreignWerewolfPolymorph"</c>
///     before Track A1 built the matching prototype) is never touched by that validator unless some
///     entity prototype explicitly re-specifies the field in YAML — so a typo'd or never-built
///     prototype id compiled clean and only failed the moment a player actually triggered the code
///     path (a silently-logged <c>TryIndex</c> miss, per Content.Server._Solreign.Antags.Werewolf
///     .SolreignWerewolfSystem.Transform.OnEnterTransformed). This test converts that failure mode
///     from "found by a player mid-round" to "found by CI before merge."
///
///     Method: reflect over every type in the three _Solreign C# namespaces (server/shared/client),
///     find every field typed EntProtoId / EntProtoId&lt;T&gt; / ProtoId&lt;T&gt; (nullable or not),
///     read its REFLECTION-DEFAULT value — direct read for <c>static</c> fields, a freshly
///     <see cref="Activator.CreateInstance(Type)"/>-constructed instance for instance fields — and,
///     for every non-null/non-empty id found, assert it resolves against that side's loaded
///     <see cref="IPrototypeManager"/>. Types that cannot be constructed parameterlessly (no public
///     zero-arg constructor, or a constructor that throws without full IoC/EntityManager context)
///     are skipped, not failed: this is a best-effort sweep of DECLARED DEFAULTS, not a 100%-coverage
///     guarantee over every runtime-assigned value — but it is exactly the check that would have
///     caught the werewolf regression at build time instead of at Grand Opening.
/// </summary>
[TestFixture]
public sealed class SolreignPrototypeIdIntegrityTest : GameTest
{
    private const string ServerNamespacePrefix = "Content.Server._Solreign";
    private const string SharedNamespacePrefix = "Content.Shared._Solreign";
    private const string ClientNamespacePrefix = "Content.Client._Solreign";

    private const BindingFlags AllFields =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.DeclaredOnly;

    /// <summary>
    ///     One discovered "id field" ready to be checked: which prototype kind it must resolve
    ///     against, and the string id its reflection-default currently holds.
    /// </summary>
    private readonly record struct IdFieldHit(string TypeName, string FieldName, Type Kind, string Id);

    [Test]
    public async Task AllSolreignEntProtoIdAndProtoIdFieldDefaults_ResolveAgainstPrototypeManager()
    {
        var server = Server;
        var client = Client;

        // Anchor types: one known type per assembly/namespace root, so we reflect the *assembly*
        // that type lives in rather than guessing at AppDomain enumeration order.
        var serverAssembly = typeof(Content.Server._Solreign.Corporate.SolreignCorporateLayerSystem).Assembly;
        var sharedAssembly = typeof(Content.Shared._Solreign.Contracts.SolreignContractsBoardComponent).Assembly;
        var clientAssembly = typeof(Content.Client._Solreign.FX.SolreignAcidBorderOverlay).Assembly;

        var skippedTypes = new List<string>();

        var serverHits = CollectHits(serverAssembly, ServerNamespacePrefix, skippedTypes);
        // Content.Shared types are loaded into (and checked against) BOTH the server and the client
        // prototype managers in a real game — but for THIS sweep, checking once against the server's
        // (which loads every prototype the client does, plus server-only ones) is sufficient; the
        // client-only assembly is checked separately below against CProtoMan.
        var sharedHits = CollectHits(sharedAssembly, SharedNamespacePrefix, skippedTypes);
        var clientHits = CollectHits(clientAssembly, ClientNamespacePrefix, skippedTypes);

        Assert.That(serverHits.Count + sharedHits.Count + clientHits.Count, Is.GreaterThan(0),
            "The reflection sweep found zero EntProtoId/ProtoId<T> field defaults anywhere under " +
            "_Solreign — that almost certainly means the type-matching logic in this test regressed " +
            "(it found 20+ the day it was written), not that the codebase stopped using typed " +
            "prototype-id fields. Check IsProtoIdField/ExtractId below before trusting a green run.");

        var failures = new List<string>();

        await server.WaitAssertion(() =>
        {
            var protoMan = server.ResolveDependency<IPrototypeManager>();
            foreach (var hit in serverHits.Concat(sharedHits))
            {
                if (!protoMan.TryIndex(hit.Kind, hit.Id, out _))
                {
                    failures.Add($"{hit.TypeName}.{hit.FieldName} = \"{hit.Id}\" " +
                                 $"(expects a {hit.Kind.Name}) does not resolve on the SERVER — " +
                                 "the werewolf lesson: this field will silently no-op the mechanic " +
                                 "that reads it instead of firing.");
                }
            }
        });

        await client.WaitAssertion(() =>
        {
            var protoMan = client.ResolveDependency<IPrototypeManager>();
            foreach (var hit in clientHits)
            {
                if (!protoMan.TryIndex(hit.Kind, hit.Id, out _))
                {
                    failures.Add($"{hit.TypeName}.{hit.FieldName} = \"{hit.Id}\" " +
                                 $"(expects a {hit.Kind.Name}) does not resolve on the CLIENT — " +
                                 "the werewolf lesson: this field will silently no-op the mechanic " +
                                 "that reads it instead of firing.");
                }
            }
        });

        if (failures.Count > 0)
            Assert.Fail(string.Join("\n", failures));
    }

    /// <summary>
    ///     Reflects over every type in <paramref name="assembly"/> whose namespace starts with
    ///     <paramref name="namespacePrefix"/>, and returns every non-null/non-empty EntProtoId /
    ///     EntProtoId&lt;T&gt; / ProtoId&lt;T&gt; field default found. Types that fail to construct
    ///     (for instance-field reads) are recorded in <paramref name="skippedTypes"/> and otherwise
    ///     ignored — this sweep is best-effort, not a coverage guarantee (see class doc comment).
    /// </summary>
    private static List<IdFieldHit> CollectHits(Assembly assembly, string namespacePrefix, List<string> skippedTypes)
    {
        var hits = new List<IdFieldHit>();

        Type?[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Best-effort, per class doc comment: a handful of unloadable types (e.g. missing
            // optional dependencies in this process) shouldn't sink the whole sweep.
            types = ex.Types;
        }

        foreach (var type in types)
        {
            if (type is null)
                continue;
            if (type.Namespace is null || !type.Namespace.StartsWith(namespacePrefix, StringComparison.Ordinal))
                continue;
            if (type.IsGenericTypeDefinition || type.IsInterface)
                continue;

            object? instance = null;
            var triedInstance = false;

            foreach (var field in type.GetFields(AllFields))
            {
                if (!TryGetPrototypeKind(field.FieldType, out var kind))
                    continue;

                object? boxedValue;
                if (field.IsStatic)
                {
                    boxedValue = field.GetValue(null);
                }
                else
                {
                    if (!triedInstance)
                    {
                        triedInstance = true;
                        instance = TryConstruct(type);
                        if (instance is null)
                            skippedTypes.Add(type.FullName ?? type.Name);
                    }

                    if (instance is null)
                        continue; // couldn't construct this type; skip its instance fields entirely

                    boxedValue = field.GetValue(instance);
                }

                if (boxedValue is null)
                    continue; // Nullable<T> with no value, or a reference-typed field left null

                var id = ExtractId(boxedValue);
                if (string.IsNullOrEmpty(id))
                    continue; // default(EntProtoId)/default(ProtoId<T>) — never assigned a real default

                hits.Add(new IdFieldHit(type.FullName ?? type.Name, field.Name, kind, id));
            }
        }

        return hits;
    }

    /// <summary>
    ///     If <paramref name="fieldType"/> is (nullable-wrapped or not) <see cref="EntProtoId"/>,
    ///     <see cref="EntProtoId{T}"/>, or <see cref="ProtoId{T}"/>, returns the <see cref="IPrototype"/>
    ///     kind it resolves against (<see cref="EntityPrototype"/> for the first two, the generic
    ///     argument for the third).
    /// </summary>
    private static bool TryGetPrototypeKind(Type fieldType, out Type kind)
    {
        kind = typeof(void);
        var unwrapped = Nullable.GetUnderlyingType(fieldType) ?? fieldType;

        if (unwrapped == typeof(EntProtoId))
        {
            kind = typeof(EntityPrototype);
            return true;
        }

        if (!unwrapped.IsGenericType)
            return false;

        var def = unwrapped.GetGenericTypeDefinition();

        if (def == typeof(EntProtoId<>))
        {
            // EntProtoId<T> still names an EntityPrototype id (T is the component it expects on it).
            kind = typeof(EntityPrototype);
            return true;
        }

        if (def == typeof(ProtoId<>))
        {
            kind = unwrapped.GetGenericArguments()[0];
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Pulls the wrapped string id off a boxed <see cref="EntProtoId"/> / <see cref="EntProtoId{T}"/>
    ///     / <see cref="ProtoId{T}"/> value via its public <c>Id</c> record property — the one field
    ///     every one of these wrapper types shares (see their own source under
    ///     RobustToolbox/Robust.Shared/Prototypes/).
    /// </summary>
    private static string? ExtractId(object boxedValue)
    {
        var idProperty = boxedValue.GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
        return idProperty?.GetValue(boxedValue) as string;
    }

    /// <summary>
    ///     Best-effort parameterless construction. Returns null (never throws) on any failure —
    ///     abstract types, missing zero-arg constructors, or constructors that require a live
    ///     IoC/EntityManager context they don't have here. See class doc comment: this makes the
    ///     sweep best-effort rather than 100%-coverage, by design.
    /// </summary>
    private static object? TryConstruct(Type type)
    {
        if (type.IsAbstract)
            return null;

        try
        {
            return Activator.CreateInstance(type, nonPublic: true);
        }
        catch
        {
            return null;
        }
    }
}
