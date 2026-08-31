namespace FuelImport.Aws.Options;

public class AwsVisionOptions
{
    public bool EnableAwsApis { get; set; }
    public string Region { get; set; } = "us-east-1";
    public string? A2iFlowDefinitionArn { get; set; }
}
