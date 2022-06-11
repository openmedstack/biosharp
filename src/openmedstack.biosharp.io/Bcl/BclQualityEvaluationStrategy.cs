/* The code in this file is migrated from the Java code in the Picard project (https://github.com/broadinstitute/picard).

The code is released under MIT license.*/

namespace OpenMedStack.BioSharp.Io.Bcl
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /**
     * Describes a mechanism for revising and evaluating qualities read from a BCL file.  This class accumulates observations about low quality
     * scores that it evaluates, so distinct instances should be used for unrelated sets of BCL readers.
     *
     * The mechanism for revising qualities is not configurable.  The qualities that are less than 1 are revised to 1, and other qualities are
     * not affected.
     *
     * This class is thread-safe and a single instance can and should be passed to {@link BclReader}s running in separate threads.
     *
     * To replicate the functionality of {@link BclReader}s prior to the introduction of this class, create a single instance passing
     * {@link #ILLUMINA_ALLEGED_MINIMUM_QUALITY} to the constructor, and then call {@link #assertMinimumQualities()} once the readers finish
     * their work.
     *
     * @author mccowan
     */
    public class BclQualityEvaluationStrategy
    {
        public const char IlluminaAllegedMinimumQuality = (char)2;
        private readonly int _minimumRevisedQuality;

        /** A thread-safe defaulting map that injects an Atomicint starting at 0 when a uninitialized key is get-ted. */
        private readonly Dictionary<char, int> _qualityCountMap = new();

        /**
         * @param minimumRevisedQuality The minimum quality that should be seen from revised qualities; controls whether or not an exception
         *                              is thrown when calling {@link #assertMinimumQualities()}
         */
        public BclQualityEvaluationStrategy(int minimumRevisedQuality)
        {
            _minimumRevisedQuality = minimumRevisedQuality;
        }

        /** The rule used to revise quality scores, which is: if it's less than 1, make it 1. */
        private static char GenerateRevisedQuality(char quality)
        {
            return (char)Math.Max((byte)quality, (byte)1);
        }

        /**
         * Accepts a quality read from a BCL file and (1) returns a 1 if the value was 0 and (2) makes a note of the provided quality if it is
         * low.  Because of (2) each record's quality should be passed only once to this method, otherwise it will be observed multiple times.
         *
         * @param quality The quality score read from the BCL
         * @return The revised new quality score
         */
        public char ReviseAndConditionallyLogQuality(char quality)
        {
            var revisedQuality = GenerateRevisedQuality(quality);
            if (quality < IlluminaAllegedMinimumQuality)
            {
                lock (_qualityCountMap)
                {
                    if (!_qualityCountMap.TryGetValue(quality, out var q))
                    {
                        _qualityCountMap[quality] = 0;
                    }

                    _qualityCountMap[quality] = ++q;
                }
            }

            return revisedQuality;
        }

        /**
         * Reviews the qualities observed thus far and throws an exception if any are below the minimum quality threshold.
         */
        public void AssertMinimumQualities()
        {
            /*
             * We're comparing revised qualities here, not observed, but the qualities that are logged in qualityCountMap are observed
             * qualities.  So as we iterate through it, convert observed qualities into their revised value.
             */
            var errorTokens = (from entry in _qualityCountMap
                where GenerateRevisedQuality(entry.Key) < _minimumRevisedQuality
                select $"quality {entry.Key} observed {entry.Value} times").ToList();

            if (errorTokens.Count > 0)
            {
                throw new Exception(
                    $"Found BCL qualities that fell beneath minimum threshold of {_minimumRevisedQuality}: {string.Join("; ", errorTokens)}.");
            }
        }

        /**
         * Returns a view of number of qualities that failed, where the key is the quality score and the value is the number of observations.
         */
        public IReadOnlyDictionary<byte, int> GetPoorQualityFrequencies()
        {
            return _qualityCountMap.ToDictionary(entry => (byte)entry.Key, entry => entry.Value);
        }
    }
}
