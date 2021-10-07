namespace OpenMedStack.BioSharp.Io.Bcl
{
    using System.Collections;
    using System.Collections.Generic;

    public class PassThroughFilter : IEnumerable<bool>
    {
        /// <inheritdoc />
        public IEnumerator<bool> GetEnumerator()
        {
            while (true)
            {
                yield return true;
            }
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}