namespace ZipPostLookup.CountryDataTools.Models.Commands;

public sealed class ImportCounters
{
    public int Processed             { get; set; }
    public int NewCodes              { get; set; }
    public int AlreadyClean          { get; set; }
    public int CoordsEnriched        { get; set; }
    public int NameDiscrepancies     { get; set; }
    public int TimezoneDiscrepancies { get; set; }
    public int AutoRejected          { get; set; }
    public int SpecialCodes          { get; set; }
    public int AbbreviationMatches   { get; set; }
    public int CuratedSkipped        { get; set; }
    public int FlaggedSkipped        { get; set; }
}
