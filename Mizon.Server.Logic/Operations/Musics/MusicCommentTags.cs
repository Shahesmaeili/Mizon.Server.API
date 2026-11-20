namespace Mizon.Server.Logic.Operations.Musics;

internal static class MusicCommentTags
{
    internal static bool IsSupported(string? type) => type is "person" or "client" or "music" or "music-video";

    internal static async Task<List<MusicCommentTag>?> ResolveAsync(MizonDatabaseContext database,
        List<MusicCommentTagRequest>? requested, CancellationToken token)
    {
        var tags = new List<MusicCommentTag>();
        if (requested is null) return tags;
        if (requested.Count > 8 || requested.Any(tag => tag is null || tag.PublicId == Guid.Empty || !IsSupported(tag.Type)))
            return null;
        foreach (var tag in requested.Distinct())
        {
            int? id = tag.Type switch
            {
                "person" => await database.Persons.AsNoTracking().Where(row => row.PublicId == tag.PublicId)
                    .Select(row => (int?)row.Id).SingleOrDefaultAsync(token),
                "client" => await database.Clients.AsNoTracking().Where(row => row.PublicId == tag.PublicId)
                    .Select(row => (int?)row.Id).SingleOrDefaultAsync(token),
                "music" => await database.Musics.AsNoTracking().Where(row => row.PublicId == tag.PublicId)
                    .Where(row => row.IsActive).Select(row => (int?)row.Id).SingleOrDefaultAsync(token),
                _ => await database.MusicVideos.AsNoTracking().Where(row => row.PublicId == tag.PublicId)
                    .Where(row => row.IsActive && row.Music!.IsActive).Select(row => (int?)row.Id).SingleOrDefaultAsync(token)
            };
            if (!id.HasValue) return null;
            tags.Add(tag.Type switch
            {
                "person" => new() { TaggedPersonId = id },
                "client" => new() { TaggedClientId = id },
                "music" => new() { TaggedMusicId = id },
                _ => new() { TaggedMusicVideoId = id }
            });
        }
        return tags;
    }

    internal static async Task LoadAsync(MizonDatabaseContext database,
        Dictionary<int, MusicCommentItemResponse> items, CancellationToken token)
    {
        var ids = items.Keys.ToArray();
        var tags = await database.MusicCommentTags.AsNoTracking().Where(tag => ids.Contains(tag.MusicCommentId))
            .OrderBy(tag => tag.Id).Select(tag => new
            {
                tag.MusicCommentId,
                Type = tag.TaggedPersonId != null ? "person" : tag.TaggedClientId != null ? "client" :
                    tag.TaggedMusicId != null ? "music" : "music-video",
                PublicId = tag.TaggedPersonId != null ? tag.TaggedPerson!.PublicId :
                    tag.TaggedClientId != null ? tag.TaggedClient!.PublicId :
                    tag.TaggedMusicId != null ? tag.TaggedMusic!.PublicId : tag.TaggedMusicVideo!.PublicId,
                Label = tag.TaggedPersonId != null ? tag.TaggedPerson!.FirstName + " " + tag.TaggedPerson.LastName :
                    tag.TaggedClientId != null ? tag.TaggedClient!.FirstName + " " + tag.TaggedClient.LastName :
                    tag.TaggedMusicId != null ? tag.TaggedMusic!.Title : tag.TaggedMusicVideo!.Music!.Title,
                NavigationPublicId = tag.TaggedMusicVideoId == null ? (Guid?)null : tag.TaggedMusicVideo!.Music!.PublicId,
                Available = (tag.TaggedMusicId == null || tag.TaggedMusic!.IsActive) &&
                    (tag.TaggedMusicVideoId == null || (tag.TaggedMusicVideo!.IsActive && tag.TaggedMusicVideo.Music!.IsActive))
            }).ToListAsync(token);

        foreach (var tag in tags)
            if (tag.Available) items[tag.MusicCommentId].Tags.Add(new(tag.Type, tag.PublicId, tag.Label, tag.NavigationPublicId));
    }
}
