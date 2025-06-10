using System;
using System.IO;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NCM3.Services
{
    public interface IS3BackupService
    {
        Task<bool> UploadBackupAsync(int routerId, string configContent, string version, string backupBy);
        Task<bool> UploadDatabaseBackupAsync(string filePath);
        Task<string> DownloadBackupAsync(string key);
        Task<bool> DeleteBackupAsync(string key);
        Task<ListObjectsV2Response> ListBackupsAsync(string prefix = "");
    }

    public class S3BackupService : IS3BackupService
    {
        private readonly IAmazonS3 _s3Client;
        private readonly ILogger<S3BackupService> _logger;
        private readonly string _bucketName;
        private readonly string _configBackupPrefix;
        private readonly string _databaseBackupPrefix;

        public S3BackupService(
            IAmazonS3 s3Client,
            IConfiguration configuration,
            ILogger<S3BackupService> logger)
        {
            _s3Client = s3Client;
            _logger = logger;
            
            _bucketName = configuration["AWS:S3:BucketName"];
            _configBackupPrefix = configuration["AWS:S3:ConfigBackupPrefix"] ?? "config-backups/";
            _databaseBackupPrefix = configuration["AWS:S3:DatabaseBackupPrefix"] ?? "db-backups/";

            _logger.LogDebug($"S3BackupService initialized. Bucket: '{_bucketName}', ConfigPrefix: '{_configBackupPrefix}', DbPrefix: '{_databaseBackupPrefix}'");

            if (string.IsNullOrEmpty(_bucketName))
            {
                _logger.LogError("AWS S3 BucketName is not configured in appsettings.json. S3 backups will fail.");
                throw new ArgumentException("AWS S3 BucketName must be configured in appsettings.json");
            }
        }

        public async Task<bool> UploadBackupAsync(int routerId, string configContent, string version, string backupBy)
        {
            _logger.LogInformation("Attempting to upload backup to S3 for RouterId: {RouterId}, Version: {Version}", routerId, version);
            if (string.IsNullOrEmpty(_bucketName))
            {
                _logger.LogError("S3 BucketName is not configured. Cannot upload backup for RouterId: {RouterId}", routerId);
                return false;
            }

            try
            {
                string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                string key = $"{_configBackupPrefix}router_{routerId}/{timestamp}_{version}.config";
                _logger.LogDebug("Generated S3 key for RouterId {RouterId}: {Key}", routerId, key);
                
                using (var stream = new MemoryStream())
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(configContent);
                    writer.Flush();
                    stream.Position = 0;
                    
                    var request = new PutObjectRequest
                    {
                        BucketName = _bucketName,
                        Key = key,
                        InputStream = stream,
                        ContentType = "text/plain",
                        Metadata = 
                        {
                            ["router-id"] = routerId.ToString(),
                            ["backup-date"] = DateTime.UtcNow.ToString("o"), // ISO 8601 format
                            ["version"] = version,
                            ["backup-by"] = backupBy
                        }
                    };
                    
                    _logger.LogDebug("Sending PutObjectRequest to S3 for key: {Key}", key);
                    var response = await _s3Client.PutObjectAsync(request);
                    _logger.LogInformation("Successfully uploaded router configuration to S3: {Key}. HTTP Status: {StatusCode}, RequestId: {RequestId}", 
                                         key, response.HttpStatusCode, response.ResponseMetadata?.RequestId);
                    return true;
                }
            }
            catch (Amazon.S3.AmazonS3Exception s3Ex)
            {
                _logger.LogError(s3Ex, "AmazonS3Exception during S3 upload for RouterId: {RouterId}. ErrorCode: {ErrorCode}, StatusCode: {StatusCode}, AWSRequestId: {AWSRequestId}, Message: {S3Message}", 
                                 routerId, s3Ex.ErrorCode, s3Ex.StatusCode, s3Ex.RequestId, s3Ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Generic error uploading router configuration to S3 for RouterId: {RouterId}. Message: {ExceptionMessage}", 
                                 routerId, ex.Message);
                return false;
            }
        }

        public async Task<bool> UploadDatabaseBackupAsync(string filePath)
        {
            _logger.LogInformation("Attempting to upload database backup to S3 from path: {FilePath}", filePath);
            if (string.IsNullOrEmpty(_bucketName))
            {
                _logger.LogError("S3 BucketName is not configured. Cannot upload database backup from: {FilePath}", filePath);
                return false;
            }
            try
            {
                if (!File.Exists(filePath))
                {
                    _logger.LogError("Database backup file not found: {FilePath}", filePath);
                    return false;
                }
                
                string fileName = Path.GetFileName(filePath);
                string key = $"{_databaseBackupPrefix}{fileName}";
                _logger.LogDebug("Generated S3 key for database backup {FileName}: {Key}", fileName, key);
                
                // Use TransferUtility for larger files
                var fileTransferUtility = new TransferUtility(_s3Client);
                
                var uploadRequest = new TransferUtilityUploadRequest
                {
                    FilePath = filePath,
                    BucketName = _bucketName,
                    Key = key,
                    // Optional: Add metadata if needed
                    Metadata = 
                    {
                        ["backup-date"] = DateTime.UtcNow.ToString("o")
                    }
                };
                
                _logger.LogDebug("Sending TransferUtilityUploadRequest to S3 for key: {Key}", key);
                await fileTransferUtility.UploadAsync(uploadRequest);
                _logger.LogInformation("Successfully uploaded database backup to S3: {Key}", key);
                return true;
            }
            catch (Amazon.S3.AmazonS3Exception s3Ex)
            {
                 _logger.LogError(s3Ex, "AmazonS3Exception during S3 database backup upload from {FilePath}. ErrorCode: {ErrorCode}, StatusCode: {StatusCode}, AWSRequestId: {AWSRequestId}, Message: {S3Message}", 
                                 filePath, s3Ex.ErrorCode, s3Ex.StatusCode, s3Ex.RequestId, s3Ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Generic error uploading database backup to S3 from {FilePath}: {ExceptionMessage}", 
                                 filePath, ex.Message);
                return false;
            }
        }

        public async Task<string> DownloadBackupAsync(string key)
        {
            try
            {
                var request = new GetObjectRequest
                {
                    BucketName = _bucketName,
                    Key = key
                };
                
                using (var response = await _s3Client.GetObjectAsync(request))
                using (var reader = new StreamReader(response.ResponseStream))
                {
                    return await reader.ReadToEndAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading backup from S3: {Key}, {ErrorMessage}", key, ex.Message);
                throw;
            }
        }

        public async Task<bool> DeleteBackupAsync(string key)
        {
            try
            {
                var request = new DeleteObjectRequest
                {
                    BucketName = _bucketName,
                    Key = key
                };
                
                await _s3Client.DeleteObjectAsync(request);
                _logger.LogInformation("Successfully deleted backup from S3: {Key}", key);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting backup from S3: {Key}, {ErrorMessage}", key, ex.Message);
                return false;
            }
        }

        public async Task<ListObjectsV2Response> ListBackupsAsync(string prefix = "")
        {
            try
            {
                var request = new ListObjectsV2Request
                {
                    BucketName = _bucketName,
                    Prefix = string.IsNullOrEmpty(prefix) ? _configBackupPrefix : prefix,
                    MaxKeys = 1000
                };
                
                return await _s3Client.ListObjectsV2Async(request);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing backups from S3: {ErrorMessage}", ex.Message);
                throw;
            }
        }
    }
}