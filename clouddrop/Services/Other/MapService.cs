using clouddrop.Models;

namespace clouddrop.Services.Other;

public class MapService : IMapService
{
    public T Map<F, T>(F source)
    {
        switch ((typeof(F), typeof(T)))
        {
            case var (fType, tType) when fType == typeof(Content) && tType == typeof(ContentMessage):
                return (T)(object)ContentToContentMessage((Content)(object)source);
            case var (fType, tType) when fType == typeof(User) && tType == typeof(UserProfileMessage):
                return (T)(object)UserToUserProfileMessage((User)(object)source);
        }

        throw new Exception("Type not supported in map-service");
    }

    private ContentMessage ContentToContentMessage(Content source)
        => new ContentMessage()
        {
            ContentType = (int)source.ContentType == 0 ? ContentTypeEnum.File : ContentTypeEnum.Folder,
            Id = source.Id,
            Name = source.Name,
            Size = source.Size,
            Parent = source.Parent != null ? new ContentMessage() { Id = source.Parent.Id } : null,
            Path = source.Path,
            Storage = source.Storage != null ? new StorageMessage() { Id = source.Storage.Id } : null
        };

    private UserProfileMessage UserToUserProfileMessage(User user)
        => new UserProfileMessage()
        {
            Id = user.Id,
            Email = user.Email,
            Name = user.Name,
            Storage = new UserProfileStorageMessage()
            {
                Id = user.Storage.Id,
                StorageQuote = user.Storage.StorageQuote,
                StorageUsed = user.Storage.StorageUsed
            }
        };
}