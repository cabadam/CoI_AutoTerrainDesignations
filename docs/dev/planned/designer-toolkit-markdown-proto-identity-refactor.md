# Designer Toolkit Markdown Proto Identity Refactor

Status: follow-up refactor. The bilingual Markdown export modes are implemented,
but the export data model still mixes collected stats with rendered display text.

## Current State

Designer Toolkit supports Markdown language modes through `MarkdownTableLanguage`:

- `English`
- `Local`
- `Both`
- `Hybrid`

`DTK.BlueprintExport` renders those modes by computing blueprint stats for a
specific `MarkdownRenderLanguage` and calling `DisplayName(...)` while building
the stats object.

The remaining weakness is that the collected stats are keyed by rendered strings:

```csharp
public SortedDictionary<string, string> MaintenanceValues;
public SortedDictionary<string, string> ConstructionValues;
public SortedDictionary<string, int> ComponentCounts;
```

This is functional for the current modes because stats can be recomputed per
language. It is not ideal because product/entity identity is lost before
rendering.

## Refactor Goal

Keep proto identity in the stats model until the final Markdown render step.

Prefer a shape like:

```csharp
public Dictionary<ProductProto, string> ConstructionValues;
public Dictionary<VirtualProductProto, string> MaintenanceValues;
public Dictionary<Proto, int> ComponentCounts;
```

Then render names at the edge:

```csharp
private static string DisplayName(Proto proto, MarkdownRenderLanguage language)
{
    LocStr loc = proto.Strings.Name;
    string local = loc.TranslatedString;
    string english = LocalizationManager.GetUsEnStringFor(loc);

    bool same = string.Equals(local, english, StringComparison.Ordinal);

    switch (language)
    {
        case MarkdownRenderLanguage.Local:
            return local;
        case MarkdownRenderLanguage.Hybrid:
            return same ? english : $"{local} ({english})";
        case MarkdownRenderLanguage.English:
        default:
            return english;
    }
}
```

## Why This Still Matters

String-keyed stats can merge unrelated products or entities if two localized
names are identical. Proto-keyed stats keep values distinct and allow sorting,
deduplication, bilingual rendering, or future export layouts to choose display
text without changing the collection pass.

## Suggested Work

1. Change `BlueprintStats` dictionaries to proto-keyed dictionaries.
2. Populate construction, maintenance, and component counts by proto identity.
3. Sort rows and folder-export dynamic columns by `DisplayName(proto, language)`
   during rendering.
4. Keep the existing output text stable for `English`, `Local`, `Both`, and
   `Hybrid`.
5. Add focused regression coverage for two protos whose localized display names
   collide, if a practical test seam exists.
