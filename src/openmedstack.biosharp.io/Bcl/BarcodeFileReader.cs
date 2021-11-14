/* The code in this file is migrated from the Java code in the Picard project (https://github.com/broadinstitute/picard).

The code is released under MIT license.*/

namespace OpenMedStack.BioSharp.Io.Bcl
{
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using Model.Bcl;

    /**
 * Reads a single barcode file line by line and returns the barcode if there was a match or NULL otherwise.
 *
 * Barcode.txt file Format (consists of tab delimited columns, 1 record per row)
 * sequence_read    Matched(Y/N)    BarcodeSequenceMatched
 *
 * sequence read          - the actual bases at barcode position
 * Matched(y/n)           - Y or N indicating if there was a barcode match
 * BarcodeSequenceMatched - matched barcode sequence (empty if read did not match one of the barcodes).
 */
    public class BarcodeFileReader : IAsyncEnumerable<BarcodeData?>
    {
        private readonly FileInfo _barcodeFile;
        private const int Y_OR_N_COLUMN = 1;
        private const int BARCODE_COLUMN = 2;

        public BarcodeFileReader(FileInfo barcodeFile)
        {
            _barcodeFile = barcodeFile;
        }

        /// <inheritdoc />
        public async IAsyncEnumerator<BarcodeData?> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            using var streamReader = new StreamReader(_barcodeFile.FullName);
            while (!streamReader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await streamReader.ReadLineAsync().ConfigureAwait(false);
                var fields = line!.Split('\t');
                yield return fields[Y_OR_N_COLUMN].Equals("Y") ? new BarcodeData(fields[BARCODE_COLUMN]) : null;
            }
        }
    }
}
