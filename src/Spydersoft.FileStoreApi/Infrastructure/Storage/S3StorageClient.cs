using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Spydersoft.FileStoreApi.Infrastructure.Storage;

public sealed class S3StorageClient : IStorageClient, IDisposable
{
    private readonly AmazonS3Client _s3;
    private readonly string _bucketName;

    public S3StorageClient(IOptions<StorageOptions> options)
    {
        var opts = options.Value;
        var config = new AmazonS3Config
        {
            ServiceURL = opts.ServiceUrl,
            ForcePathStyle = true,
            SignatureVersion = "4",
            AuthenticationRegion = opts.Region,
        };
        _s3 = new AmazonS3Client(new BasicAWSCredentials(opts.AccessKey, opts.SecretKey), config);
        _bucketName = opts.BucketName;
    }

    public async Task<string> GenerateUploadUrlAsync(string storageKey, string contentType, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = storageKey,
            Verb = HttpVerb.PUT,
            Expires = DateTime.UtcNow.Add(ttl),
            ContentType = contentType,
        };
        return await _s3.GetPreSignedURLAsync(request);
    }

    public async Task<string> GenerateDownloadUrlAsync(string storageKey, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = storageKey,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(ttl),
        };
        return await _s3.GetPreSignedURLAsync(request);
    }

    public async Task<bool> ObjectExistsAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        try
        {
            await _s3.GetObjectMetadataAsync(_bucketName, storageKey, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task DeleteObjectAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        await _s3.DeleteObjectAsync(_bucketName, storageKey, cancellationToken);
    }

    public void Dispose() => _s3.Dispose();
}
