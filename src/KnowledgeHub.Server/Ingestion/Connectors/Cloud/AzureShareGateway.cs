using System.Runtime.CompilerServices;
using Azure.Storage;
using Azure.Storage.Files.Shares;
using Azure.Storage.Files.Shares.Models;

namespace KnowledgeHub.Server.Ingestion.Connectors.Cloud;

/// <summary>Azure Files share gateway — recursive directory walk over
/// Azure.Storage.Files.Shares (<see cref="AzureFilesConnector"/>).</summary>
internal sealed class AzureShareGateway(ShareClient share, string rootDirectory) : IRemoteObjectGateway
{
    public async IAsyncEnumerable<RemoteObject> ListAsync(
        string? prefix, [EnumeratorCancellation] CancellationToken ct)
    {
        var root = string.IsNullOrWhiteSpace(prefix) ? rootDirectory : $"{rootDirectory}/{prefix}".Trim('/');
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var dirPath = pending.Pop();
            ShareDirectoryClient dir = dirPath.Length == 0 ? share.GetRootDirectoryClient() : share.GetDirectoryClient(dirPath);
            await foreach (var item in dir.GetFilesAndDirectoriesAsync(cancellationToken: ct))
            {
                var itemPath = dirPath.Length == 0 ? item.Name : $"{dirPath}/{item.Name}";
                if (item.IsDirectory)
                {
                    pending.Push(itemPath);
                    continue;
                }
                var file = dir.GetFileClient(item.Name);
                var props = await file.GetPropertiesAsync(ct);
                yield return new RemoteObject(
                    itemPath,
                    props.Value.ETag.ToString(),
                    props.Value.LastModified,
                    props.Value.ContentLength);
            }
        }
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct)
    {
        var file = share.GetRootDirectoryClient().GetFileClient(key);
        return await file.OpenReadAsync(new ShareFileOpenReadOptions(allowModifications: false), ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>From a full connection string, or account name + key.</summary>
    public static AzureShareGateway Create(
        string shareName, string? directoryPath, string? connectionString,
        string? accountName, string? accountKey)
    {
        ShareClient share;
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            share = new ShareClient(connectionString, shareName);
        }
        else
        {
            var credential = new StorageSharedKeyCredential(accountName!, accountKey!);
            share = new ShareClient(
                new Uri($"https://{accountName}.file.core.windows.net/{shareName}"), credential);
        }
        return new AzureShareGateway(share, (directoryPath ?? "").Trim('/'));
    }
}
