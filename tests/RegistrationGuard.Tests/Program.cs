using SilverpineMods.QOLNPCSelector;

// Runs the production guard without Unity. The fixture reproduces native
// dictionary.Add/Remove and Player Character UI allocation order, with faults
// injected at the points that previously removed another registration.
var cases = new (string Name, Action<Fixture> Run)[]
{
    ("duplicate preserves existing instance and never calls Load/Unload", f =>
    {
        Definition existing = f.Create();
        existing.Load();
        Definition candidate = f.Create();
        Throws<InvalidOperationException>(() => f.LoadGuarded(candidate));
        Check(ReferenceEquals(f.Registered, existing), "existing instance lost");
        Check(candidate.LoadCalls == 0 && candidate.UnloadCalls == 0,
            "duplicate invoked native lifecycle");
        Check(existing.UiCreated == f.PlayerCharacter, "existing UI changed");
    }),
    ("loading the same instance twice does not clean it up", f =>
    {
        Definition candidate = f.Create();
        f.LoadGuarded(candidate);
        Throws<InvalidOperationException>(() => f.LoadGuarded(candidate));
        Check(ReferenceEquals(f.Registered, candidate), "same instance lost");
        Check(candidate.LoadCalls == 1 && candidate.UnloadCalls == 0,
            "same instance reloaded or unloaded");
    }),
    ("successful load and rollback affect only the new instance", f =>
    {
        Definition existing = f.Create("Other");
        existing.Load();
        Definition candidate = f.Create();
        f.LoadGuarded(candidate);
        Check(ReferenceEquals(f.Registered, candidate), "load not registered");
        f.UnloadGuarded(candidate);
        Check(f.Registered == null && !candidate.UiCreated, "rollback incomplete");
        Check(ReferenceEquals(f.Registry["Other"], existing), "other NPC lost");
    }),
    ("failure before registration releases partially created UI", f =>
    {
        Definition candidate = f.Create();
        var original = new IOException("Failed before dictionary.Add");
        candidate.BeforeRegistration = () => throw original;
        Exception result = Throws<IOException>(() => f.LoadGuarded(candidate));
        Check(ReferenceEquals(result, original), "load exception replaced");
        Check(f.Registered == null && !candidate.UiCreated, "partial state leaked");
        Check(candidate.UnloadCalls == 1, "partial UI cleanup was skipped");
    }),
    ("failure after registration removes its own registration", f =>
    {
        Definition candidate = f.Create();
        var original = new InvalidOperationException("Selection callback failed");
        candidate.AfterRegistration = () => throw original;
        Exception result = Throws<InvalidOperationException>(
            () => f.LoadGuarded(candidate));
        Check(ReferenceEquals(result, original), "load exception replaced");
        Check(f.Registered == null && !candidate.UiCreated, "failed load retained");
    }),
    ("cleanup preserves a foreign registration and reports incomplete cleanup", f =>
    {
        Definition candidate = f.Create();
        Definition replacement = f.Create();
        var original = new IOException("Duplicate appeared during Load");
        candidate.BeforeRegistration = () =>
        {
            replacement.Load();
            throw original;
        };
        AggregateException result = Throws<AggregateException>(
            () => f.LoadGuarded(candidate));
        Check(ReferenceEquals(f.Registered, replacement), "foreign instance removed");
        Check(candidate.UnloadCalls == 0, "unsafe cleanup called native Unload");
        Check(result.InnerExceptions.Count == 2
            && ReferenceEquals(result.InnerExceptions[0], original),
            "original failure or ownership error lost");
    }),
    ("batch rollback refuses to remove a replacement instance", f =>
    {
        Definition candidate = f.Create();
        f.LoadGuarded(candidate);
        Definition replacement = f.Create();
        f.Registry[candidate.Name] = replacement;
        Throws<InvalidOperationException>(() => f.UnloadGuarded(candidate));
        Check(ReferenceEquals(f.Registered, replacement), "replacement lost");
        Check(candidate.UnloadCalls == 0, "unsafe rollback called native Unload");
    }),
    ("cleanup failure retains both exception causes", f =>
    {
        Definition candidate = f.Create();
        var loadError = new IOException("Load failed");
        var cleanupError = new InvalidOperationException("Cleanup failed");
        candidate.AfterRegistration = () => throw loadError;
        candidate.AfterUnload = () => throw cleanupError;
        AggregateException result = Throws<AggregateException>(
            () => f.LoadGuarded(candidate));
        Check(ReferenceEquals(result.InnerExceptions[0], loadError)
            && ReferenceEquals(result.InnerExceptions[1], cleanupError),
            "cleanup failure was swallowed");
    })
};

int passed = 0;
foreach (bool playerCharacter in new[] { false, true })
{
    foreach (var test in cases)
    {
        var fixture = new Fixture(playerCharacter);
        test.Run(fixture);
        Console.WriteLine($"PASS [{(playerCharacter ? "Player Character" : "NPC")}] {test.Name}");
        passed++;
    }
}
Console.WriteLine($"{passed} registration-guard regression tests passed.");

static T Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T exception) { return exception; }
    throw new Exception("Expected " + typeof(T).Name);
}

static void Check(bool condition, string failure)
{
    if (!condition)
        throw new Exception(failure);
}

sealed class Fixture(bool playerCharacter)
{
    public bool PlayerCharacter { get; } = playerCharacter;
    public Dictionary<string, Definition> Registry { get; } = new();
    public Definition? Registered => Find("Test");
    public Definition Create(string name = "Test") => new(this, name);
    private Definition? Find(string name) => Registry.GetValueOrDefault(name);

    public void LoadGuarded(Definition candidate) => DefinitionRegistrationGuard.Load(
        candidate, candidate.Name, () => Find(candidate.Name),
        definition => definition.Load(), definition => definition.Unload());

    public void UnloadGuarded(Definition candidate) => DefinitionRegistrationGuard.UnloadOwned(
        candidate, candidate.Name, () => Find(candidate.Name),
        definition => definition.Unload());
}

sealed class Definition(Fixture fixture, string name)
{
    public string Name { get; } = name;
    public bool UiCreated { get; private set; }
    public int LoadCalls { get; private set; }
    public int UnloadCalls { get; private set; }
    public Action? BeforeRegistration { get; set; }
    public Action? AfterRegistration { get; set; }
    public Action? AfterUnload { get; set; }

    public void Load()
    {
        LoadCalls++;
        UiCreated = fixture.PlayerCharacter;
        BeforeRegistration?.Invoke();
        fixture.Registry.Add(Name, this);
        AfterRegistration?.Invoke();
    }

    public void Unload()
    {
        UnloadCalls++;
        UiCreated = false;
        fixture.Registry.Remove(Name);
        AfterUnload?.Invoke();
    }
}
