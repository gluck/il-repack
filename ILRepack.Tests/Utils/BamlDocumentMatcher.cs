using Confuser.Renamer.BAML;
using Fasterflect;
using NUnit.Framework.Constraints;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace ILRepack.Tests.Utils
{
    /// <summary>
    /// An ad-hoc matcher for the BamlDocument. If it's ugly, then one reason is
    /// that the Constraint architecture is pretty weird :)
    ///
    /// We need it in order to check just the required information from a BAML stream,
    /// since we don't care about things like line information (at least for the
    /// current usage which generates a known set of records).
    /// </summary>
    internal sealed class BamlDocumentMatcher : Constraint
    {
        private readonly BamlDocument _expectedDocument;

        private readonly Dictionary<Type, List<string>> _propertiesToCheck = new Dictionary<Type, List<string>>
        {
            { typeof(AssemblyInfoRecord), new List<string> { "AssemblyFullName", "AssemblyId" } },
            { typeof(AttributeInfoRecord), new List<string> { "AttributeId", "Name", "AttributeUsage", "OwnerTypeId" } },
            { typeof(ElementStartRecord), new List<string> { "Flags", "TypeId" } },
            { typeof(XmlnsPropertyRecord), new List<string> { "Prefix", "XmlNamespace", "AssemblyIds" } },
            { typeof(PropertyComplexStartRecord), new List<string> { "AttributeId" } },
            { typeof(PropertyRecord), new List<string> { "AttributeId", "Value" } },
            { typeof(PropertyCustomRecord), new List<string> { "AttributeId", "SerializerTypeId", "Data" } },
            { typeof(PropertyWithConverterRecord), new List<string> { "AttributeId", "ConverterTypeId", "Value" } },
            //TODO: Too lazy to do reference-only check for this.
            //   { typeof(DeferableContentStartRecord), new List<string> { "Record" } }
        };

        public BamlDocumentMatcher(BamlDocument expectedDocument)
        {
            _expectedDocument = expectedDocument;
            Description = "The two BamlDocuments match in their signature, version and records";
        }

        public override ConstraintResult ApplyTo<TActual>(TActual actual)
        {
            var bamlDocument = actual as BamlDocument;
            if (bamlDocument == null)
            {
                return new ConstraintResult(this, actual, ConstraintStatus.Failure);
            }

            if (!Equals(bamlDocument.Signature, _expectedDocument.Signature))
            {
                return new ConstraintResult(this, actual, ConstraintStatus.Failure);
            }

            if (!HaveSameVersion("ReaderVersion", bamlDocument, _expectedDocument) ||
                !HaveSameVersion("WriterVersion", bamlDocument, _expectedDocument) ||
                !HaveSameVersion("UpdaterVersion", bamlDocument, _expectedDocument))
            {
                return new ConstraintResult(this, actual, ConstraintStatus.Failure);
            }

            var areRecordsEquivalent = AreRecordsEquivalent(
                GetRelevantRecords(_expectedDocument),
                GetRelevantRecords(bamlDocument));
            return new ConstraintResult(this, actual, areRecordsEquivalent);
        }

        private bool HaveSameVersion(
            string versionProperty, BamlDocument actualDocument, BamlDocument expectedDocument)
        {
            var actualVersion = (BamlDocument.BamlVersion)actualDocument.GetPropertyValue(versionProperty);
            var expectedVersion = (BamlDocument.BamlVersion)expectedDocument.GetPropertyValue(versionProperty);

            return actualVersion.Major == expectedVersion.Major &&
                   actualVersion.Minor == expectedVersion.Minor;
        }

        private bool AreRecordsEquivalent(
            List<BamlRecord> expectedRecords, List<BamlRecord> actualRecords)
        {
            if (expectedRecords.Count != actualRecords.Count)
            {
                return false;
            }

            return expectedRecords.Zip(actualRecords, Tuple.Create).All(
                tuple => AreEquivalent(tuple.Item1, tuple.Item2));
        }

        private bool AreEquivalent(BamlRecord expectedRecord, BamlRecord actualRecord)
        {
            if (expectedRecord.GetType() != actualRecord.GetType() ||
                expectedRecord.Type != actualRecord.Type)
                return false;

            List<string> propertiesToCheck;
            if (!TryGetRecordPropertiesToCheck(expectedRecord, out propertiesToCheck))
                return true;

            foreach (string propertyName in propertiesToCheck)
            {
                var expectedValue = expectedRecord.GetPropertyValue(propertyName);
                var actualValue = actualRecord.GetPropertyValue(propertyName);

                if (!Equals(expectedValue, actualValue))
                {
                    if (!Equals(expectedValue as IEnumerable, actualValue as IEnumerable))
                        return false;
                }
            }

            return true;
        }

        private bool TryGetRecordPropertiesToCheck(BamlRecord expectedRecord, out List<string> propertiesToCheck)
        {
            foreach (var pair in _propertiesToCheck)
            {
                var type = pair.Key;
                if (type.IsInstanceOfType(expectedRecord))
                {
                    propertiesToCheck = pair.Value;
                    return true;
                }
            }

            propertiesToCheck = null;
            return false;
        }

        private bool Equals(IEnumerable expected, IEnumerable actual)
        {
            if (expected != null && actual != null)
            {
                return expected.Cast<object>().SequenceEqual(actual.Cast<object>());
            }

            return false;
        }

        private List<BamlRecord> GetRelevantRecords(BamlDocument document)
        {
            return document.Where(node => !(node is LineNumberAndPositionRecord || node is LinePositionRecord)).ToList();
        }
    }
}
