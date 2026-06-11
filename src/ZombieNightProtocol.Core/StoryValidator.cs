namespace ZombieNightProtocol.Core;

public sealed class StoryValidator
{
    public IReadOnlyList<StoryValidationIssue> Validate(StoryPackage story)
    {
        var issues = new List<StoryValidationIssue>();
        var duplicateIds = story.Scenes
            .GroupBy(scene => scene.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        foreach (var duplicateId in duplicateIds)
        {
            issues.Add(new StoryValidationIssue("Error", "duplicate_scene", $"Sahne kimliği tekrar ediyor: {duplicateId}"));
        }

        if (duplicateIds.Count > 0)
        {
            return issues;
        }

        var sceneIds = story.Scenes.Select(scene => scene.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!sceneIds.Contains(story.StartSceneId))
        {
            issues.Add(new StoryValidationIssue("Error", "missing_start", $"Başlangıç sahnesi bulunamadı: {story.StartSceneId}"));
            return issues;
        }

        foreach (var scene in story.Scenes)
        {
            foreach (var choice in scene.Choices)
            {
                if (!string.IsNullOrWhiteSpace(choice.NextSceneId) && !sceneIds.Contains(choice.NextSceneId))
                {
                    issues.Add(new StoryValidationIssue("Error", "missing_next", $"{scene.Id}/{choice.Id} olmayan sahneye bağlı: {choice.NextSceneId}"));
                }
            }
        }

        var reachable = FindReachable(story, sceneIds);
        foreach (var sceneId in sceneIds.Except(reachable, StringComparer.OrdinalIgnoreCase))
        {
            issues.Add(new StoryValidationIssue("Warning", "unreachable", $"Ulaşılamayan sahne: {sceneId}"));
        }

        foreach (var cycle in FindCycles(story))
        {
            issues.Add(new StoryValidationIssue("Info", "cycle", $"Sahne döngüsü: {string.Join(" -> ", cycle)}"));
        }

        return issues;
    }

    private static HashSet<string> FindReachable(StoryPackage story, HashSet<string> sceneIds)
    {
        var scenes = story.Scenes.ToDictionary(scene => scene.Id, StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        queue.Enqueue(story.StartSceneId);
        while (queue.TryDequeue(out var id))
        {
            if (!visited.Add(id) || !scenes.TryGetValue(id, out var scene))
            {
                continue;
            }

            foreach (var next in scene.Choices.Select(choice => choice.NextSceneId).Where(next => next is not null && sceneIds.Contains(next)))
            {
                queue.Enqueue(next!);
            }
        }
        return visited;
    }

    private static IEnumerable<IReadOnlyList<string>> FindCycles(StoryPackage story)
    {
        var scenes = story.Scenes.ToDictionary(scene => scene.Id, StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = new List<string>();
        var cycles = new List<IReadOnlyList<string>>();

        void Visit(string id)
        {
            if (active.Contains(id))
            {
                var index = path.FindIndex(item => item.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    cycles.Add(path.Skip(index).Append(id).ToList());
                }
                return;
            }
            if (!visited.Add(id) || !scenes.TryGetValue(id, out var scene))
            {
                return;
            }

            active.Add(id);
            path.Add(id);
            foreach (var next in scene.Choices.Select(choice => choice.NextSceneId).Where(next => !string.IsNullOrWhiteSpace(next)))
            {
                Visit(next!);
            }
            path.RemoveAt(path.Count - 1);
            active.Remove(id);
        }

        Visit(story.StartSceneId);
        return cycles;
    }
}
