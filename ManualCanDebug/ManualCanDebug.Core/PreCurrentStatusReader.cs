using System;
using System.Collections.Generic;

namespace ManualCanDebug.Core
{
    public static class PreCurrentStatusReader
    {
        public static IReadOnlyList<PreCurrentReadResult> ReadAll(
            IReadOnlyList<PreCurrentReadItem> readItems,
            Func<uint, int, int, double> readValue)
        {
            if (readItems == null) throw new ArgumentNullException(nameof(readItems));
            if (readValue == null) throw new ArgumentNullException(nameof(readValue));

            List<PreCurrentReadResult> results = new List<PreCurrentReadResult>();
            foreach (PreCurrentReadItem item in readItems)
            {
                try
                {
                    double value = readValue(item.AddressOffset, item.TableIndex, item.DataSize);
                    results.Add(PreCurrentReadResult.Success(item, value, item.Interpret(value)));
                }
                catch (Exception ex)
                {
                    results.Add(PreCurrentReadResult.Failure(item, ex.Message));
                }
            }

            return results.AsReadOnly();
        }
    }
}
