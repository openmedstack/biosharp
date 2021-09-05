namespace OpenMedStack.BioSharp.Io.Vcf
{
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Model;

    public class VariantReader
    {
        private readonly ILogger _logger;

        public VariantReader(ILogger logger)
        {
            _logger = logger;
        }

        public Task<Variant> Read(string line)
        {
            _logger.LogDebug("Reading: {line}", line);
            var parts = line.Split('\t');
            return Task.FromResult(new Variant
            {
                Chromosome = parts[0],
                Position = int.Parse(parts[1]),
                MarkerIdentifiers = parts[2],
                Reference = parts[3][0],
                Alternate = parts[4],
                ErrorProbabilities = GetProbabilities(parts[5]),
                FailedFilter = new[] { parts[6] },
                AdditionalInformation = parts[7]
            });
        }

        private static int[] GetProbabilities(string part)
        {
            return part.Split('/').Select(p => p == "." ? 0 : int.Parse(p)).ToArray();
        }
    }
}