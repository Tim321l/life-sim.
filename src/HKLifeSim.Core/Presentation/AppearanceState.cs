namespace HKLifeSim.Core.Presentation;

public enum Stage
{
    Baby,
    Child,
    Teen,
    Adult,
    Elder,
    Tombstone,
}

// Happy is never returned by AppearanceCalculator.Calculate — it is a transient state the UI
// layer overlays for one animation cycle after a successful action (see Phase 7 §D/§Q6), since
// GameState carries no "last action was positive" signal. Calculate only ever produces
// Dead/Sick/Stressed/Tired/Idle.
public enum Mood
{
    Idle,
    Happy,
    Tired,
    Stressed,
    Sick,
    Dead,
}

public readonly record struct AppearanceState(Stage Stage, Mood Mood);
