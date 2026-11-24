using System.Collections;
using UnityEngine;

/// <summary>
/// Living Map R1 + run card. At run start: reads the level seed from <see cref="DungeonLayout"/>, picks ONE
/// <see cref="RunModifier"/> from <see cref="_pool"/> deterministically from that seed (same seed → same
/// mutator, so good runs can be shared), applies it, and shows a run card through
/// <see cref="MissionMessageUI.Show"/>: a place name made from the seed + the starting area, the mutator
/// and the seed. Put it on the GameManager next to the <see cref="TensionDirector"/>.
/// </summary>
[DefaultExecutionOrder(-20)]
[DisallowMultipleComponent]
[AddComponentMenu("Fluffterror/Mission/Run Setup")]
public class RunSetup : MonoBehaviour
{
    [SerializeField] private RunModifier[] _pool;
    [Tooltip("Debug: always use this modifier instead of the seeded pick.")]
    [SerializeField] private RunModifier _forceModifier;
    [Tooltip("Debug: -1 = seeded pick, otherwise index into the pool.")]
    [SerializeField] private int _forceIndex = -1;

    [Header("Run card")]
    [SerializeField] private bool _showRunCard = true;
    [SerializeField] private float _cardDelay = 0.8f;
    [SerializeField] private float _cardHold = 6f;
    [SerializeField] private MissionMessageUI _messages;

    [Header("Place names (adjective + noun by start area)")]
    [SerializeField] private string[] _adjectives =
        { "Drowned", "Hollow", "Forgotten", "Weeping", "Rotten", "Silent", "Crooked", "Starving", "Gnawed", "Sunken", "Mouldy", "Whispering" };
    [SerializeField] private string[] _gothicNouns = { "Abbey", "Ruins", "Cloister", "Keep" };
    [SerializeField] private string[] _graveyardNouns = { "Graves", "Boneyard", "Churchyard" };
    [SerializeField] private string[] _hellNouns = { "Furnace", "Pit", "Forge" };
    [SerializeField] private string[] _catacombNouns = { "Crypt", "Ossuary", "Catacombs" };
    [SerializeField] private string[] _cavernNouns = { "Grotto", "Caves", "Hollows" };

    public RunModifier Active { get; private set; }
    public string PlaceName { get; private set; }
    public int Seed { get; private set; }

    private RunContext _ctx;

    private void Start()
    {
        var layout = DungeonLayout.Current != null ? DungeonLayout.Current : FindAnyObjectByType<DungeonLayout>();
        Seed = layout != null ? layout.Seed : 0;
        _ctx = new RunContext
        {
            Seed = Seed,
            Rng = new System.Random(Mix(Seed ^ 0x40D1F1E5)),
            Layout = layout,
            Moods = FindAnyObjectByType<LightingMoodController>(),
            Director = TensionDirector.Instance != null ? TensionDirector.Instance : FindAnyObjectByType<TensionDirector>(),
            Spawner = FindAnyObjectByType<EnemySpawner>(),
        };

        Active = Pick(_ctx.Rng);
        PlaceName = MakePlaceName(layout, new System.Random(Mix(Seed ^ 0x9A7E)));
        if (Active != null) Active.Apply(_ctx);
        Debug.Log($"[RunSetup] seed {Seed}: \"{PlaceName}\" — {(Active != null ? Active.DisplayName : "no modifier")}", this);

        if (_showRunCard) StartCoroutine(ShowCard());
    }

    private void OnDestroy()
    {
        if (Active != null && _ctx != null) Active.Remove(_ctx);
    }

    /// <summary>Integer hash (lowbias32). System.Random's first outputs are strongly correlated for nearby
    /// seeds (30 and 11 both drew "Whispering"), so run-level picks hash the seed first.</summary>
    public static int Mix(int x)
    {
        unchecked
        {
            uint h = (uint)x;
            h ^= h >> 16; h *= 0x7feb352du;
            h ^= h >> 15; h *= 0x846ca68bu;
            h ^= h >> 16;
            return (int)(h & 0x7fffffff);
        }
    }

    private RunModifier Pick(System.Random rng)
    {
        if (_forceModifier != null) return _forceModifier;
        if (_pool == null || _pool.Length == 0) return null;
        if (_forceIndex >= 0 && _forceIndex < _pool.Length) return _pool[_forceIndex];
        float total = 0f;
        foreach (var m in _pool) if (m != null) total += m.Weight;
        if (total <= 0f) return null;
        double roll = rng.NextDouble() * total;
        foreach (var m in _pool)
        {
            if (m == null) continue;
            roll -= m.Weight;
            if (roll <= 0) return m;
        }
        return _pool[_pool.Length - 1];
    }

    private string MakePlaceName(DungeonLayout layout, System.Random rng)
    {
        var type = layout != null ? layout.SectionAt(layout.PlayerStart, out _) : MultiSectionDungeonGenerator.SectionType.Transition;
        if (type == MultiSectionDungeonGenerator.SectionType.Transition && layout != null && layout.Sections.Count > 0)
            type = layout.Sections[rng.Next(layout.Sections.Count)].Type;
        string[] nouns = type switch
        {
            MultiSectionDungeonGenerator.SectionType.Graveyard => _graveyardNouns,
            MultiSectionDungeonGenerator.SectionType.HellPillars => _hellNouns,
            MultiSectionDungeonGenerator.SectionType.Catacombs => _catacombNouns,
            MultiSectionDungeonGenerator.SectionType.Cavern => _cavernNouns,
            _ => _gothicNouns,
        };
        string adj = _adjectives.Length > 0 ? _adjectives[rng.Next(_adjectives.Length)] : "";
        string noun = nouns.Length > 0 ? nouns[rng.Next(nouns.Length)] : "Dungeon";
        return $"The {adj} {noun}".Replace("  ", " ");
    }

    private IEnumerator ShowCard()
    {
        yield return new WaitForSeconds(_cardDelay);
        if (_messages == null) _messages = FindAnyObjectByType<MissionMessageUI>();
        if (_messages == null) yield break;
        string sub = Active != null
            ? $"{Active.DisplayName} — {Active.Description}     seed {Seed}"
            : $"seed {Seed}";
        _messages.Show(PlaceName, sub, Active != null ? Active.CardColor : new Color(0.91f, 0.89f, 0.82f), false, _cardHold);
    }
}
