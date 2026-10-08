using Android.Content;
using Android.Provider;
using Mdk.Formats;
using Uri = Android.Net.Uri;

namespace Mdk.Android;

/// <summary>A folder the user picked (Storage Access Framework), read through its content URIs:
/// shared storage has no file paths the game could open.</summary>
internal sealed class DocumentTree : IDataTree
{
    private static readonly string[] Columns =
    [
        DocumentsContract.Document.ColumnDocumentId,
        DocumentsContract.Document.ColumnDisplayName,
        DocumentsContract.Document.ColumnMimeType,
        DocumentsContract.Document.ColumnSize,
    ];

    private readonly ContentResolver _resolver;
    private readonly Uri _tree;
    /// <summary>Document ids by relative path ("" the picked folder), learnt while listing.</summary>
    private readonly Dictionary<string, string> _ids = [];

    public DocumentTree(ContentResolver resolver, Uri tree)
    {
        _resolver = resolver;
        _tree = tree;
        _ids[""] = DocumentsContract.GetTreeDocumentId(tree)!;
    }

    public IReadOnlyList<DataEntry> List(string folder)
    {
        var children = DocumentsContract.BuildChildDocumentsUriUsingTree(_tree, _ids[folder])!;
        var entries = new List<DataEntry>();
        using var cursor = _resolver.Query(children, Columns, null, null, null);
        if (cursor == null)
        {
            return entries;
        }

        while (cursor.MoveToNext())
        {
            var id = cursor.GetString(0)!;
            var name = cursor.GetString(1)!;
            var kind = cursor.GetString(2) == DocumentsContract.Document.MimeTypeDir ? EntryKind.Folder : EntryKind.File;
            var size = cursor.IsNull(3) ? DataEntry.UnknownSize : cursor.GetLong(3);
            _ids[folder.Length == 0 ? name : folder + "/" + name] = id;
            entries.Add(new DataEntry(name, kind, size));
        }

        return entries;
    }

    public Stream Open(string file)
    {
        var uri = DocumentsContract.BuildDocumentUriUsingTree(_tree, _ids[file])!;
        return _resolver.OpenInputStream(uri) ?? throw new IOException($"Can't open {file}");
    }
}
