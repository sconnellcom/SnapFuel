namespace FuelImport.Core.Models;

public enum ImageType
{
    Unknown = 0,
    Pump = 1,
    Dashboard = 2,
    FuelReceipt = 3,
    OtherReceipt = 4
}

public enum ProcessingStatus
{
    Discovered = 0,
    MetadataExtracted = 1,
    Classified = 2,
    Paired = 3,
    OcrCompleted = 4,
    Completed = 5,
    SkippedDuplicate = 6,
    Failed = 7
}

public enum ReviewStatus
{
    Pending = 0,
    Reviewed = 1,
    Rejected = 2,
    Corrected = 3,
    AutoDetected = 4
}

public enum EntrySource
{
    Manual = 0,
    AutoDetected = 1
}
