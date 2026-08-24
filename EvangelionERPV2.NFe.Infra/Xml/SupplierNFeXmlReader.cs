using System.Globalization;
using System.Xml.Linq;

namespace EvangelionERPV2.NFeModule.Infra.Xml
{
    /// <summary>One line of a supplier's nota.</summary>
    public sealed record SupplierNFeItem(
        string Code,
        string? Ean,
        string Description,
        decimal Quantity,
        decimal UnitValue,
        string UnitOfMeasure,
        decimal LineAmount);

    /// <summary>One duplicata — the nota's own instalment schedule.</summary>
    public sealed record SupplierNFeDuplicata(string Number, DateTime? DueDate, decimal Amount);

    /// <summary>Everything the import needs out of an inbound NF-e, and nothing else.</summary>
    public sealed record SupplierNFeDocument(
        string AccessKey,
        string Number,
        string Series,
        DateTime IssuedAt,
        decimal TotalAmount,
        string EmitterDocument,
        string EmitterName,
        string EmitterAddress,
        IReadOnlyList<SupplierNFeDuplicata> Duplicatas,
        IReadOnlyList<SupplierNFeItem> Items);

    /// <summary>
    /// Reads a supplier's NF-e XML into a plain record. Parse only: no I/O, no database, no
    /// certificate, no SEFAZ.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>System.Xml.Linq</c> rather than the fiscal library's deserializer. The import needs
    /// eleven values out of a document whose layout has been frozen since NF-e 4.00, and reading
    /// them by name keeps this file pure and trivially testable — an inbound nota from a stranger's
    /// ERP is exactly the input that arrives slightly wrong, and a strict object deserializer
    /// refuses the whole document over a field nothing here reads.
    /// </para>
    /// <para>
    /// It accepts both a <c>nfeProc</c> (the authorized nota, protocol attached — what a supplier
    /// actually emails) and a bare <c>NFe</c>. Anything else is a <see cref="FormatException"/>:
    /// the caller holds the file and can look at it.
    /// </para>
    /// </remarks>
    public static class SupplierNFeXmlReader
    {
        private static readonly XNamespace Nfe = "http://www.portalfiscal.inf.br/nfe";
        private const int AccessKeyLength = 44;

        public static SupplierNFeDocument Read(byte[] xml)
        {
            if (xml == null || xml.Length == 0)
                throw new FormatException("The file is empty.");

            XDocument document;
            try
            {
                using var stream = new MemoryStream(xml);
                // No DTD processing and no external resolver: this is a file from outside.
                using var reader = System.Xml.XmlReader.Create(stream, new System.Xml.XmlReaderSettings
                {
                    DtdProcessing = System.Xml.DtdProcessing.Prohibit,
                    XmlResolver = null
                });

                document = XDocument.Load(reader);
            }
            catch (Exception ex) when (ex is System.Xml.XmlException or ArgumentException)
            {
                throw new FormatException("The file is not valid XML.", ex);
            }

            var infNFe = document.Descendants(Nfe + "infNFe").FirstOrDefault()
                ?? throw new FormatException("The file does not contain an NF-e.");

            var ide = Required(infNFe, "ide");
            var emit = Required(infNFe, "emit");
            var icmsTotal = document.Descendants(Nfe + "ICMSTot").FirstOrDefault()
                ?? throw new FormatException("The NF-e has no totals.");

            var accessKey = ReadAccessKey(document, infNFe);
            var emitterDocument = OnlyDigits(Text(emit, "CNPJ") ?? Text(emit, "CPF"));
            if (emitterDocument.Length == 0)
                throw new FormatException("The NF-e has no emitter document.");

            return new SupplierNFeDocument(
                AccessKey: accessKey,
                Number: Text(ide, "nNF") ?? string.Empty,
                Series: Text(ide, "serie") ?? string.Empty,
                IssuedAt: ReadDate(Text(ide, "dhEmi") ?? Text(ide, "dEmi"))
                          ?? throw new FormatException("The NF-e has no issue date."),
                TotalAmount: ReadDecimal(Text(icmsTotal, "vNF")),
                EmitterDocument: emitterDocument,
                EmitterName: Text(emit, "xNome") ?? Text(emit, "xFant") ?? "Supplier",
                EmitterAddress: ReadAddress(emit),
                Duplicatas: ReadDuplicatas(infNFe),
                Items: ReadItems(infNFe));
        }

        /// <summary>
        /// The chave, from the protocol when the nota is authorized and from <c>infNFe/@Id</c>
        /// otherwise. Both carry it; the protocol is the one SEFAZ signed.
        /// </summary>
        private static string ReadAccessKey(XDocument document, XElement infNFe)
        {
            var fromProtocol = OnlyDigits(document.Descendants(Nfe + "infProt")
                .Select(x => x.Element(Nfe + "chNFe")?.Value)
                .FirstOrDefault());

            if (fromProtocol.Length == AccessKeyLength)
                return fromProtocol;

            var fromId = OnlyDigits(infNFe.Attribute("Id")?.Value);
            if (fromId.Length == AccessKeyLength)
                return fromId;

            throw new FormatException("The NF-e has no access key.");
        }

        private static IReadOnlyList<SupplierNFeDuplicata> ReadDuplicatas(XElement infNFe)
        {
            return infNFe.Descendants(Nfe + "dup")
                .Select(dup => new SupplierNFeDuplicata(
                    Text(dup, "nDup") ?? string.Empty,
                    ReadDate(Text(dup, "dVenc")),
                    ReadDecimal(Text(dup, "vDup"))))
                .ToList();
        }

        private static IReadOnlyList<SupplierNFeItem> ReadItems(XElement infNFe)
        {
            return infNFe.Elements(Nfe + "det")
                .Select(det => det.Element(Nfe + "prod"))
                .Where(prod => prod != null)
                .Select(prod => new SupplierNFeItem(
                    Code: Text(prod!, "cProd") ?? string.Empty,
                    // "SEM GTIN" is the literal the layout prescribes when there is no barcode.
                    Ean: NormalizeEan(Text(prod!, "cEAN")),
                    Description: Text(prod!, "xProd") ?? string.Empty,
                    Quantity: ReadDecimal(Text(prod!, "qCom")),
                    UnitValue: ReadDecimal(Text(prod!, "vUnCom")),
                    UnitOfMeasure: Text(prod!, "uCom") ?? string.Empty,
                    LineAmount: ReadDecimal(Text(prod!, "vProd"))))
                .ToList();
        }

        private static string ReadAddress(XElement emit)
        {
            var enderEmit = emit.Element(Nfe + "enderEmit");
            if (enderEmit == null)
                return string.Empty;

            var parts = new[]
            {
                Text(enderEmit, "xLgr"),
                Text(enderEmit, "nro"),
                Text(enderEmit, "xBairro"),
                Text(enderEmit, "xMun"),
                Text(enderEmit, "UF")
            };

            return string.Join(", ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        }

        private static XElement Required(XElement parent, string name)
        {
            return parent.Element(Nfe + name)
                ?? throw new FormatException($"The NF-e has no <{name}>.");
        }

        private static string? Text(XElement parent, string name) => parent.Element(Nfe + name)?.Value?.Trim();

        private static string? NormalizeEan(string? value)
        {
            var digits = OnlyDigits(value);
            return digits.Length == 0 ? null : digits;
        }

        private static string OnlyDigits(string? value)
        {
            return value == null ? string.Empty : new string(value.Where(char.IsDigit).ToArray());
        }

        /// <summary>Always invariant: the layout fixes the decimal point, whatever the reader's locale.</summary>
        private static decimal ReadDecimal(string? value)
        {
            return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0m;
        }

        private static DateTime? ReadDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            // A bare date first, and as UTC: dVenc is "2026-04-10" with no offset, and letting the
            // general parser read it as local midnight moves the due date a day backwards for any
            // server east of Greenwich.
            if (DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
                return DateTime.SpecifyKind(dateOnly, DateTimeKind.Utc);

            // dhEmi (4.00) carries its own offset, so it converts honestly.
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var withOffset))
                return withOffset.UtcDateTime;

            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var plain)
                ? plain
                : null;
        }
    }
}
