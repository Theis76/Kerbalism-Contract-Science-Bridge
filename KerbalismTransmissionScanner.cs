using System;
using System.Reflection;

namespace KerbalismContractScienceBridge
{
    /// <summary>
    /// Reads the minimal stable data needed from Kerbalism SubjectData.
    ///
    /// Expected Kerbalism 3.32 members:
    /// - SubjectData.StockSubjectId
    /// - SubjectData.ExpInfo
    /// - ExperimentInfo.ExperimentId
    ///
    /// All accesses are reflection-based and validated.
    /// </summary>
    internal static class KerbalismSubjectReader
    {
        internal static bool TryRead(
            object subjectData,
            out string stockSubjectId,
            out string experimentId)
        {
            stockSubjectId = null;
            experimentId = null;

            if (subjectData == null)
                return false;

            stockSubjectId = ReflectionUtil.Get<string>(
                subjectData, "StockSubjectId", null);

            object expInfo = ReflectionUtil.Get(subjectData, "ExpInfo");
            if (expInfo == null)
                expInfo = ReflectionUtil.Get(subjectData, "ExperimentInfo");

            if (expInfo != null)
            {
                experimentId = ReflectionUtil.Get<string>(
                    expInfo, "ExperimentId", null);
            }

            // Some versions expose the experiment through a lower-case field
            // or the calling Experiment module itself. The caller may provide
            // a fallback experiment ID.
            return !string.IsNullOrEmpty(stockSubjectId);
        }
    }
}
