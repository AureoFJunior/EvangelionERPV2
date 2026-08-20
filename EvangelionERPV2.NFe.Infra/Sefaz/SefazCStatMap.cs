using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.NFeModule.Infra.Sefaz
{
    /// <summary>How this system records one SEFAZ answer.</summary>
    /// <param name="Status">The status to write. <see cref="NFeStatus.Pending"/> means "do not change what is stored".</param>
    /// <param name="IsTransient">The exchange failed for a reason worth retrying, not because of a verdict.</param>
    /// <param name="NeverTransmitted">SEFAZ has no record of the chave, so the same number is safe to send again.</param>
    public readonly record struct SefazVerdict(NFeStatus Status, bool IsTransient, bool NeverTransmitted);

    /// <summary>
    /// Translates a cStat into a <see cref="SefazVerdict"/>. Pure, dependency-free and split by the
    /// operation that produced the code, because the same number does not mean the same thing on an
    /// authorization, a consult and an event.
    /// </summary>
    /// <remarks>
    /// The distinction that matters most here is transient versus rejected. <see cref="NFeStatus.Rejected"/>
    /// is a verdict and it burns the number permanently — re-issuing after one takes a new number.
    /// A timeout or a paralysed autorizador is an accident, and recording it as a rejection would
    /// throw away a perfectly usable number every time SEFAZ hiccups.
    /// </remarks>
    public static class SefazCStatMap
    {
        #region cStat constants

        /// <summary>Autorizado o uso da NF-e.</summary>
        public const int Autorizado = 100;

        /// <summary>Autorizado o uso da NF-e, fora de prazo. Authorized all the same.</summary>
        public const int AutorizadoForaDePrazo = 150;

        /// <summary>Cancelamento de NF-e homologado.</summary>
        public const int CancelamentoHomologado = 101;

        /// <summary>Cancelamento homologado fora de prazo.</summary>
        public const int CancelamentoHomologadoForaDePrazo = 151;

        /// <summary>Evento registrado e vinculado a NF-e. One of the two cancellation homologations.</summary>
        public const int EventoRegistrado = 135;

        /// <summary>Evento registrado, vinculado a NF-e, fora de prazo. The other one.</summary>
        public const int EventoRegistradoForaDePrazo = 155;

        /// <summary>Uso denegado. Terminal: the key is burned, it cannot be cancelled and cannot be reused.</summary>
        public const int UsoDenegado = 110;

        /// <summary>Uso denegado: irregularidade fiscal do emitente.</summary>
        public const int DenegadoEmitente = 301;

        /// <summary>Uso denegado: irregularidade fiscal do destinatário.</summary>
        public const int DenegadoDestinatario = 302;

        /// <summary>Uso denegado: operação com destinatário não habilitado.</summary>
        public const int DenegadoDestinatarioNaoHabilitado = 303;

        /// <summary>Lote em processamento. The outcome is not known yet — it is not a rejection.</summary>
        public const int LoteEmProcessamento = 105;

        /// <summary>Serviço em operação. The healthy answer to StatusServico.</summary>
        public const int ServicoEmOperacao = 107;

        /// <summary>Serviço paralisado momentaneamente.</summary>
        public const int ServicoParalisadoMomentaneamente = 108;

        /// <summary>Serviço paralisado sem previsão.</summary>
        public const int ServicoParalisadoSemPrevisao = 109;

        /// <summary>NF-e não consta na base de dados da SEFAZ. The note never landed.</summary>
        public const int NaoConstaNaBase = 217;

        /// <summary>Rejeição: erro não catalogado. SEFAZ itself does not know, so neither do we.</summary>
        public const int ErroNaoCatalogado = 999;

        /// <summary>Rejeição: duplicidade de evento. The cancellation is already registered.</summary>
        public const int DuplicidadeDeEvento = 573;

        #endregion

        /// <summary>Verdict on the cStat carried by an authorization response or its protocol.</summary>
        public static SefazVerdict ForAuthorization(int cStat) => cStat switch
        {
            Autorizado or AutorizadoForaDePrazo => new SefazVerdict(NFeStatus.Authorized, false, false),
            UsoDenegado or DenegadoEmitente or DenegadoDestinatario or DenegadoDestinatarioNaoHabilitado
                => new SefazVerdict(NFeStatus.Denied, false, false),

            // Still in the queue: the note may yet be authorized, so nothing terminal may be
            // written and the reconciler has to ask again through the chave.
            LoteEmProcessamento => Transient(),

            _ => Fallback(cStat)
        };

        /// <summary>Verdict on the cStat carried by a ConsultaProtocolo response.</summary>
        public static SefazVerdict ForConsult(int cStat) => cStat switch
        {
            Autorizado or AutorizadoForaDePrazo => new SefazVerdict(NFeStatus.Authorized, false, false),

            // A consult is also how a cancellation already homologated at SEFAZ comes back. Reading
            // these as a rejection would overwrite a cancelled note with a burnt-number verdict.
            CancelamentoHomologado or CancelamentoHomologadoForaDePrazo or EventoRegistrado or EventoRegistradoForaDePrazo
                => new SefazVerdict(NFeStatus.Cancelled, false, false),

            UsoDenegado or DenegadoEmitente or DenegadoDestinatario or DenegadoDestinatarioNaoHabilitado
                => new SefazVerdict(NFeStatus.Denied, false, false),

            // The one answer that releases a number. SEFAZ has never seen this chave, so whatever
            // the write-ahead marker says, nothing arrived: Error is the retryable state and the
            // same number may be sent again rather than needing inutilização paperwork. This is the
            // only place NeverTransmitted is ever true — everywhere else, after transmission, we
            // cannot prove nothing left.
            NaoConstaNaBase => new SefazVerdict(NFeStatus.Error, IsTransient: false, NeverTransmitted: true),

            LoteEmProcessamento => Transient(),

            _ => Fallback(cStat)
        };

        /// <summary>Verdict on the cStat carried by a RecepcaoEvento response for event 110111.</summary>
        /// <remarks>
        /// 135 and 155 are the only two homologations of a cancellation. Everything else leaves the
        /// note authorized and still carrying its tax liability, which is why the caller must not
        /// write <see cref="NFeStatus.Cancelled"/> on anything but these.
        /// </remarks>
        public static SefazVerdict ForCancellation(int cStat) => cStat switch
        {
            EventoRegistrado or EventoRegistradoForaDePrazo => new SefazVerdict(NFeStatus.Cancelled, false, false),

            // "Já registrado" is not a refusal. The cancel path commits CancelRequestedAt before
            // the round trip, so a retry after a lost answer lands here — and calling that a
            // rejection would leave a note cancelled at SEFAZ recorded as Authorized, which is the
            // exact lie the cancel rework exists to prevent. Note that no protocol comes back with
            // it: the caller keeps whatever it already stored, or consults to fill it in.
            DuplicidadeDeEvento => new SefazVerdict(NFeStatus.Cancelled, false, false),

            _ => Fallback(cStat)
        };

        /// <summary>
        /// True only for cStat 107, "Serviço em Operação". Used to tell "SEFAZ is down" from "our
        /// request was wrong". A bool rather than a verdict because no <see cref="NFeStatus"/>
        /// describes an autorizador — there is no document involved.
        /// </summary>
        public static bool IsServiceInOperation(int cStat) => cStat == ServicoEmOperacao;

        /// <summary>Nothing changes about the stored document; the exchange is worth repeating.</summary>
        /// <remarks>
        /// <c>NeverTransmitted</c> is deliberately false. These codes come back <em>after</em> a
        /// request went out, and nothing about them proves the bytes never left.
        /// </remarks>
        private static SefazVerdict Transient() => new(NFeStatus.Pending, IsTransient: true, NeverTransmitted: false);

        private static SefazVerdict Fallback(int cStat) => cStat switch
        {
            ServicoParalisadoMomentaneamente or ServicoParalisadoSemPrevisao or ErroNaoCatalogado => Transient(),

            // ponytail: a single conservative catch-all. SEFAZ publishes hundreds of rejection
            // rules and not one line of this mapping has round-tripped against a real certificate
            // yet (docs/fiscal/SPEC-remaining-code.md §2.1 — any cStat mapping written now ships
            // unverified), so the codes above are the ones whose meaning is worth encoding and
            // everything else is recorded verbatim as a rejection with its own cStat and xMotivo
            // for a human to read. Split specific families out as real rejections are observed.
            _ => new SefazVerdict(NFeStatus.Rejected, false, false)
        };
    }
}
