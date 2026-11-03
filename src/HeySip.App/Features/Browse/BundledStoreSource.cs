using Avalonia.Platform;
using HeySip.Core.Features.Companion.Store;

namespace HeySip.App.Features.Browse;

/// <summary>
/// The list that comes inside the app: <c>Assets/Store/catalog.json</c>, in exactly the format a server would send, with
/// pack files next to it. Swapping this for a source that reads the same format from a web address changes nothing else.
/// </summary>
public sealed class BundledStoreSource : IStoreSource
{
    private const string Folder = "avares://HeySip/Assets/Store/";

    public Task<StoreCatalogResult> LoadAsync(CancellationToken cancel)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri(Folder + "catalog.json"));
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return Task.FromResult(StoreCatalog.Parse(buffer.ToArray()));
        }
        catch (Exception exception) when (exception is IOException or FileNotFoundException)
        {
            return Task.FromResult(StoreCatalogResult.Failure("The list couldn't be opened"));
        }
    }

    public Task<byte[]> FetchPackAsync(StoreEntry entry, CancellationToken cancel)
    {
        if (entry.PackLocation is not { } location)
        {
            throw new IOException("This item has no file to download");
        }

        try
        {
            using var stream = AssetLoader.Open(new Uri(Folder + location));
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return Task.FromResult(buffer.ToArray());
        }
        catch (Exception exception) when (exception is FileNotFoundException or UriFormatException)
        {
            throw new IOException("The file isn't in the list", exception);
        }
    }
}
