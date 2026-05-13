using System.IO;
using System.Text;
using System.Threading.Tasks;
using OpenMedStack.BioSharp.Model;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

public class SampleSheetTests
{
    private const string SingleSampleCsv = """
                                           [Header]
                                           IEMFileVersion,4
                                           Investigator Name,Test
                                           Date,2024-01-01
                                           Workflow,GenerateFASTQ
                                           [Reads]
                                           151
                                           151
                                           [Settings]
                                           [Data]
                                           Lane,Sample_ID,Sample_Name,Sample_Plate,Sample_Well,I7_Index_ID,index,I5_Index_ID,index2,Sample_Project,Description
                                           1,S1,SampleOne,,A01,N701,TAAGGCGA,S501,TAGATCGC,Project1,Test sample
                                           """;

    private const string MultiSampleCsv = """
                                          [Header]
                                          IEMFileVersion,4
                                          [Reads]
                                          151
                                          [Settings]
                                          [Data]
                                          Lane,Sample_ID,Sample_Name,I7_Index_ID,index,Sample_Project
                                          1,S1,SampleOne,N701,TAAGGCGA,Proj1
                                          1,S2,SampleTwo,N702,CGTACTAG,Proj1
                                          """;

    [Fact]
    public async Task SampleSheetReader_ParsesSingleSampleSheet()
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(SingleSampleCsv));
        var sheet = await SampleSheetReader.Read(ms);

        Assert.Single(sheet.Samples);
        Assert.Equal("S1", sheet.Samples[0].SampleId);
        Assert.Equal("SampleOne", sheet.Samples[0].SampleName);
        Assert.Equal("TAAGGCGA", sheet.Samples[0].Index1);
        Assert.Equal("TAGATCGC", sheet.Samples[0].Index2);
        Assert.Equal("Project1", sheet.Samples[0].Project);
    }

    [Fact]
    public async Task SampleSheetReader_ParsesMultiSampleSheet()
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(MultiSampleCsv));
        var sheet = await SampleSheetReader.Read(ms);

        Assert.Equal(2, sheet.Samples.Count);
        Assert.Equal("S1", sheet.Samples[0].SampleId);
        Assert.Equal("S2", sheet.Samples[1].SampleId);
    }

    [Fact]
    public async Task SampleSheetReader_ParsesHeaderSection()
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(SingleSampleCsv));
        var sheet = await SampleSheetReader.Read(ms);

        Assert.Equal("4", sheet.Header["IEMFileVersion"]);
        Assert.Equal("GenerateFASTQ", sheet.Header["Workflow"]);
    }

    [Fact]
    public async Task SampleSheetReader_ParsesReadsSection()
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(SingleSampleCsv));
        var sheet = await SampleSheetReader.Read(ms);

        Assert.Equal(2, sheet.ReadLengths.Count);
        Assert.Equal(151, sheet.ReadLengths[0]);
        Assert.Equal(151, sheet.ReadLengths[1]);
    }
}