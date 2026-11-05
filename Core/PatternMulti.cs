namespace PatMaxSharp.Core;

/// <summary>
/// Multi-pattern model (equivalent to ccPMMultiModel / CogPMAlignMultiTool).
///
/// Reconstructed from reports/06-train-run.md:
///   object layout: map +0x48, trained +0x50, modelIdQueue +0x58, statWindow +0x74
///   run modes: sequential / most recently successful / most successful / exhaustive
///   pipeline: cfMultiRun -> ccPMMultiModel::run -> per-model -> scoring kernel
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
    private readonly Dictionary<string, PatternModel> _map = new();

    public MultiRunMode RunMode { get; set; } = MultiRunMode.Sequential;
    public double ConfusionThreshold { get; set; } = 0.1;

    public int Count => _models.Count;
    public bool Trained => _models.Count > 0;
    public IReadOnlyList<PatternModel> Models => _models;

    /// <summary>Add (or replace) a model by id (cfMultiAddModel, map dedup).</summary>
    public void AddModel(string id, PatternModel model)
    {
        if (_map.TryGetValue(id, out var existing))
        {
            int idx = _models.IndexOf(existing);
            if (idx >= 0) _models[idx] = model;
        }
        else
        {
            _models.Add(model);
        }
        _map[id] = model;
    }

    public bool RemoveModel(string id)
    {
        if (!_map.Remove(id, out var model)) return false;
        _models.Remove(model);
        return true;
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
