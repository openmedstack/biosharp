namespace OpenMedStack.BioSharp.Io.Sam;

public enum CigarOp: byte
{
    Match = 0,
    Insertion = 1,
    Deletion = 2,
    Skip = 3,
    SoftClip = 4,
    HardClip = 5,
    Padding = 6,
    Equal = 7,
    Difference = 8
}
