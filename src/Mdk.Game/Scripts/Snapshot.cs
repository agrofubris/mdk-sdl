using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Mdk.Formats;
using Mdk.Game.Objects;

namespace Mdk.Game.Scripts;

/// <summary>Packs and unpacks objects' public fields as JSON for full saves (F2, the original's
/// <c>AREN</c>/<c>ALIE</c> packets; snapshot.gd). Values become plain data:
/// <code>
///   MdkObject in the object list  → {"#obj": index}
///   arena script object            → {"#fixed": key}   ("arena:NAME", "hits:NAME")
///   ModelAnimation                 → {"#anim": name}
///   Vector3, Matrix4x4, (int, int) → arrays of numbers;  non-finite floats → strings
///   the model, sound voices        → left out (found again by the type, restarted by name)
/// </code></summary>
public sealed class Snapshot
{
    private const string ObjectKey = "#obj";
    private const string FixedKey = "#fixed";
    private const string AnimationKey = "#anim";
    private const int HashHexDigits = 16;
    /// <summary>Fields rebuilt by their owner rather than saved.</summary>
    private static readonly string[] Skipped = [nameof(MdkObject.Model), nameof(MdkObject.LoopSound), nameof(MdkObject.TrackedVoice)];

    private readonly IReadOnlyList<MdkObject?> _objects;
    private readonly Dictionary<MdkObject, int> _index = [];
    private readonly IReadOnlyDictionary<string, MdkObject> _fixed;
    private readonly Dictionary<MdkObject, string> _fixedKeys = [];
    private readonly Func<MdkObject?, string, ModelAnimation?> _findAnimation;

    /// <summary><paramref name="objects"/> are referred to by index, <paramref name="fixedObjects"/>
    /// (key → object) by key; <paramref name="findAnimation"/> turns an animation's name back into
    /// the animation for an object.</summary>
    public Snapshot(IReadOnlyList<MdkObject?> objects, IReadOnlyDictionary<string, MdkObject> fixedObjects,
        Func<MdkObject?, string, ModelAnimation?> findAnimation)
    {
        _objects = objects;
        for (var i = 0; i < objects.Count; i++)
        {
            if (objects[i] is { } obj)
            {
                _index[obj] = i;
            }
        }

        _fixed = fixedObjects;
        foreach (var (key, obj) in fixedObjects)
        {
            _fixedKeys[obj] = key;
        }

        _findAnimation = findAnimation;
    }

    /// <summary>A short, stable hash of a JSON text (tests compare a save with its reload).</summary>
    public static string Hash(string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..HashHexDigits].ToLowerInvariant();

    /// <summary>The public instance fields of an object, as plain data.</summary>
    public JsonObject Pack<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] T>(T obj) where T : class
    {
        var data = new JsonObject();
        foreach (var field in Fields(typeof(T)))
        {
            data[field.Name] = Encode(field.GetValue(obj));
        }

        return data;
    }

    /// <summary>Sets an object's fields back; read-only arrays are filled in place.</summary>
    public void Unpack<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] T>(T obj, JsonObject data, MdkObject? owner = null)
        where T : class
    {
        foreach (var field in Fields(typeof(T)))
        {
            if (!data.TryGetPropertyValue(field.Name, out var node))
            {
                continue;
            }

            var value = Decode(node, field.FieldType, owner ?? obj as MdkObject);
            if (field.IsInitOnly)
            {
                if (field.GetValue(obj) is Array target && value is Array source)
                {
                    Array.Copy(source, target, Math.Min(source.Length, target.Length));
                }

                continue;
            }

            field.SetValue(obj, value);
        }
    }

    private static IEnumerable<FieldInfo> Fields([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Instance).Where(f => !Skipped.Contains(f.Name));

    public JsonNode? Encode(object? value) => value switch
    {
        null => null,
        MdkObject obj => _index.TryGetValue(obj, out var i) ? new JsonObject { [ObjectKey] = i }
            : _fixedKeys.TryGetValue(obj, out var key) ? new JsonObject { [FixedKey] = key } : null,
        ModelAnimation animation => new JsonObject { [AnimationKey] = animation.Name },
        Model => null,
        int i => i,
        bool b => b,
        string s => s,
        float f => Number(f),
        Vector2 v => new JsonArray(Number(v.X), Number(v.Y)),
        Vector3 v => new JsonArray(Number(v.X), Number(v.Y), Number(v.Z)),
        Matrix4x4 m => new JsonArray(Enumerable.Range(0, 16).Select(k => Number(m[k / 4, k % 4])).ToArray()),
        ValueTuple<int, int> t => new JsonArray(t.Item1, t.Item2),
        System.Collections.IEnumerable list => new JsonArray(list.Cast<object?>().Select(Encode).ToArray()),
        _ => null,
    };

    /// <summary>A float, or its text when JSON can't hold it (NaN, infinities).</summary>
    private static JsonNode Number(float f) => float.IsFinite(f) ? JsonValue.Create(f) : JsonValue.Create(f.ToString(CultureInfo.InvariantCulture));

    private static float Float(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? float.Parse(text, CultureInfo.InvariantCulture) : node?.GetValue<float>() ?? 0f;

    /// <summary>A value of type <paramref name="type"/> (null: the type's default); <paramref
    /// name="owner"/> is the object it belongs to (animations are looked up in its arena).</summary>
    public object? Decode(JsonNode? node, Type type, MdkObject? owner)
    {
        if (node == null)
        {
            return null;
        }

        if (type == typeof(int))
        {
            return node.GetValue<int>();
        }

        if (type == typeof(float))
        {
            return Float(node);
        }

        if (type == typeof(bool))
        {
            return node.GetValue<bool>();
        }

        if (type == typeof(string))
        {
            return node.GetValue<string>();
        }

        if (type == typeof(Vector2))
        {
            return new Vector2(Float(node[0]), Float(node[1]));
        }

        if (type == typeof(Vector3))
        {
            return new Vector3(Float(node[0]), Float(node[1]), Float(node[2]));
        }

        if (type == typeof(Matrix4x4))
        {
            var m = new Matrix4x4();
            for (var k = 0; k < 16; k++)
            {
                m[k / 4, k % 4] = Float(node[k]);
            }

            return m;
        }

        if (type == typeof(ValueTuple<int, int>))
        {
            return (node[0]!.GetValue<int>(), node[1]!.GetValue<int>());
        }

        if (type == typeof(MdkObject))
        {
            return Reference(node.AsObject());
        }

        if (type == typeof(ModelAnimation))
        {
            return _findAnimation(owner, node[AnimationKey]!.GetValue<string>());
        }

        return DecodeList(node.AsArray(), type, owner);
    }

    private MdkObject? Reference(JsonObject data)
    {
        if (data.TryGetPropertyValue(ObjectKey, out var index))
        {
            var i = index!.GetValue<int>();
            return i >= 0 && i < _objects.Count ? _objects[i] : null;
        }

        return data.TryGetPropertyValue(FixedKey, out var key) ? _fixed.GetValueOrDefault(key!.GetValue<string>()) : null;
    }

    /// <summary>The arrays and lists objects have (no reflection on element types, for AOT).</summary>
    private object? DecodeList(JsonArray array, Type type, MdkObject? owner)
    {
        if (type == typeof(float[]))
        {
            return array.Select(Float).ToArray();
        }

        if (type == typeof(int[]))
        {
            return array.Select(n => n!.GetValue<int>()).ToArray();
        }

        if (type == typeof(string[]))
        {
            return array.Select(n => n!.GetValue<string>()).ToArray();
        }

        if (type == typeof(Vector3[]))
        {
            return array.Select(n => (Vector3)Decode(n, typeof(Vector3), owner)!).ToArray();
        }

        if (type == typeof(List<int>))
        {
            return array.Select(n => n!.GetValue<int>()).ToList();
        }

        return null;
    }
}
