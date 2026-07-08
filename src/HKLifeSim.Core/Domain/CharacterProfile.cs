namespace HKLifeSim.Core.Domain;

public enum Gender
{
    Male,
    Female,
    Other,
}

public sealed record CharacterProfile(
    string Name,
    Gender Gender,
    int BirthYear);
