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
    internal class BamlDocumentMatcher : Constraint
    {
        private readonly BamlDocument _expectedDocument;
        private object _actualValue, _expectedValue, _context;

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
        }

        public override bool Matches(object actual)
        {
            var bamlDocument = (BamlDocument)actual;

            if (!AreEqual(bamlDocument.Signature, _expectedDocument.Signature))
            {
                _context = "Document Signature";
                return false;
            }

            if (!HaveSameVersion("ReaderVersion", bamlDocument, _expectedDocument) ||
                !HaveSameVersion("WriterVersion", bamlDocument, _expectedDocument) ||
                !HaveSameVersion("UpdaterVersion", bamlDocument, _expectedDocument))
            {
                return false;
            }

            return AreRecordsEquivalent(
                GetRelevantRecords(_expectedDocument),
                GetRelevantRecords(bamlDocument));
        }

        private bool HaveSameVersion(
            string versionProperty, BamlDocument actualDocument, BamlDocument expectedDocument)
        {
            var actualVersion = (BamlDocument.BamlVersion)actualDocument.GetPropertyValue(versionProperty);
            var expectedVersion = (BamlDocument.BamlVersion)expectedDocument.GetPropertyValue(versionProperty);

            _context = versionProperty + ".Major";
            if (!AreEqual(actualVersion.Major, actualVersion.Major))
                return false;

            _context = versionProperty + ".Minor";
            return AreEqual(actualVersion.Minor, expectedVersion.Minor);
        }

        private bool AreEqual(object expected, object actual)
        {
            _expectedValue = expected;
            _actualValue = actual;

            return Equals(_expectedValue, _actualValue);
        }

        private bool AreRecordsEquivalent(
            List<BamlRecord> expectedRecords, List<BamlRecord> actualRecords)
        {
            if (!AreEqual(expectedRecords.Count, actualRecords.Count))
            {
                _context = "total number of records";
                return false;
            }

            return expectedRecords.Zip(actualRecords, Tuple.Create).All(
                tuple => AreEquivalent(tuple.Item1, tuple.Item2));
        }

        private bool AreEquivalent(BamlRecord expectedRecord, BamlRecord actualRecord)
        {
            _context = actualRecord;
            if (!AreEqual(expectedRecord.GetType(), actualRecord.GetType()) ||
                !AreEqual(expectedRecord.Type, actualRecord.Type))
                return false;

            List<string> propertiesToCheck;
            if (!TryGetRecordPropertiesToCheck(expectedRecord, out propertiesToCheck))
                return true;

            foreach (string propertyName in propertiesToCheck)
            {
                _context = string.Format("Property '{0}' of '{1}'", propertyName, actualRecord);

                _expectedValue = expectedRecord.GetPropertyValue(propertyName);
                _actualValue = actualRecord.GetPropertyValue(propertyName);

                if (!Equals(_expectedValue, _actualValue))
                {
                    if (!Equals(_expectedValue as IEnumerable, _actualValue as IEnumerable))
                        return false;
                }
            }

            return true;
        }

        private bool TryGetRecordPropertiesToCheck(BamlRecord expectedRecord, out List<string> propertiesToCheck)
        {
            foreach (var pair in _propertiesToCheck)
            {
                Type type = pair.Key;
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

        public override void WriteActualValueTo(MessageWriter writer)
        {
            writer.WriteActualValue(_actualValue);
        }

        public override void WriteDescriptionTo(MessageWriter writer)
        {
            writer.WriteExpectedValue(_expectedValue);

            if (_context != null)
                writer.WriteMessageLine("In " + _context);
        }
    }
}
