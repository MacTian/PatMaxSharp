namespace PatMaxSharp.Core;

/// <summary>
/// How a multi-pattern set is searched (the multi-pattern model is the counterpart of
/// CogPMAlignMultiTool).
///
/// Early analysis notes also named a native ccPMMultiModel and quoted its layout
/// (map +0x48, trained +0x50, modelIdQueue +0x58, statWindow +0x74) plus a pipeline
/// "cfMultiRun -> ccPMMultiModel::run -> per-model -> scoring kernel". The native core is
/// WIBU-packed, so those offsets and internal names cannot be verified and are not claimed
/// here; they are kept only as vocabulary.
///
/// Run modes as implemented by SelectModels (all of them are picks over the current model
/// list, not over run history):
///   Sequential             - every model, in registration order
///   MostRecentlySuccessful - the first model whose last score is greater than 0
///   MostSuccessful         - the model with the highest last score
///   Exhaustive             - every model (identical to Sequential in this implementation)
/// </summary>
public enum MultiRunMode
{
    Sequential = 0,
    MostRecentlySuccessful = 1,
    MostSuccessful = 2,
    Exhaustive = 3
}

public sealed class PatternMulti
{
    private readonly List<PatternModel> _models = new();
    private readonly List<string> _ids = new();
    private readonly Dictionary<string, PatternModel> _map = new();

    /// <summary>Which selection rule <see cref="SelectModels"/> applies.</summary>
    public MultiRunMode RunMode { get; set; } = MultiRunMode.Sequential;

    /// <summary>
    /// Score margin below which two models count as confused. Declared for API compatibility;
    /// the current selection rules do not consult it.
    /// </summary>
    public double ConfusionThreshold { get; set; } = 0.1;

    /// <summary>Number of registered models.</summary>
    public int Count => _models.Count;

    /// <summary>True when at least one model is registered.</summary>
    public bool Trained => _models.Count > 0;

    /// <summary>Models in registration order.</summary>
    public IReadOnlyList<PatternModel> Models => _models;

    /// <summary>Add (or replace) a model by id (cfMultiAddModel, map dedup).</summary>
    public void AddModel(string id, PatternModel model)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));
        id ??= $"model-{_models.Count}";

        if (_map.TryGetValue(id, out var existing))
        {
            int idx = _models.IndexOf(existing);
            if (idx >= 0) { _models[idx] = model; _ids[idx] = id; }
        }
        else
        {
            _models.Add(model);
            _ids.Add(id);
        }
        _map[id] = model;
    }

    /// <summary>Unregister a model by id; returns false when the id was never registered.</summary>
    public bool RemoveModel(string id)
    {
        if (!_map.Remove(id, out var model)) return false;
        int idx = _models.IndexOf(model);
        if (idx >= 0) { _models.RemoveAt(idx); _ids.RemoveAt(idx); }
        return true;
    }

    /// <summary>Id of a registered model (used to tag results).</summary>
    public string IdOf(PatternModel model)
    {
        int idx = _models.IndexOf(model);
        return idx >= 0 ? _ids[idx] : null;
    }

    /// <summary>Select which models to run based on the multi-run mode.</summary>
    public IEnumerable<PatternModel> SelectModels(Func<PatternModel, double> lastScore)
    {
        switch (RunMode)
        {
            case MultiRunMode.Sequential:
                return _models;
            case MultiRunMode.MostRecentlySuccessful:
                return _models.Where(m => lastScore(m) > 0).Take(1);
            case MultiRunMode.MostSuccessful:
                return _models.OrderByDescending(lastScore).Take(1);
            case MultiRunMode.Exhaustive:
            default:
                return _models;
        }
    }

    public override string ToString() => $"PatternMulti(models={_models.Count}, mode={RunMode})";
}
