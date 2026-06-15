namespace MSAVA_INF.Contexts;

public interface IPublicFileMetadataStore
{
    Guid? CheckPublicDownloadAccess(byte[] fileHash, string fileExtension);
}
