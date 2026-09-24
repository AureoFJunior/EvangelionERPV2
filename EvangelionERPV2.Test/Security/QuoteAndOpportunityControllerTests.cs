using AutoMapper;
using EvangelionERPV2.OpportunityRadarModule.Application.Interface;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace EvangelionERPV2.Test.Security
{
    public class QuoteControllerTests
    {
        private readonly Guid _enterpriseId = Guid.NewGuid();
        private readonly Mock<IQuoteService> _service = new();

        private QuoteController Controller(bool canOverrideCredit = false)
        {
            var authorization = new Mock<IAuthorizationService>();
            authorization
                .Setup(a => a.AuthorizeAsync(
                    It.IsAny<System.Security.Claims.ClaimsPrincipal>(), It.IsAny<object?>(), It.IsAny<string>()))
                .ReturnsAsync(canOverrideCredit ? AuthorizationResult.Success() : AuthorizationResult.Failed());

            return new QuoteController(_service.Object, authorization.Object, new Mock<IMapper>().Object)
                .WithTenant(_enterpriseId);
        }

        private QuoteController Anonymous() =>
            new QuoteController(_service.Object, new Mock<IAuthorizationService>().Object, new Mock<IMapper>().Object)
                .WithoutTenant();

        private Quote NewQuote() => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = _enterpriseId,
            Number = 42,
            Status = (int)EnumQuoteStatus.Draft,
            TotalValue = 100m,
            Items = []
        };

        [Fact]
        public async Task GetQuotes_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().GetQuotes());
        }

        [Fact]
        public async Task GetQuotes_ForwardsTheStatusFilterAndTheCallersTenant()
        {
            _service.Setup(s => s.GetByEnterpriseAsync(_enterpriseId, (int)EnumQuoteStatus.Sent, It.IsAny<CancellationToken>()))
                .ReturnsAsync([NewQuote()]);

            Assert.IsType<OkObjectResult>(await Controller().GetQuotes((int)EnumQuoteStatus.Sent));
            _service.VerifyAll();
        }

        [Fact]
        public async Task GetQuote_ThatIsNotThisTenants_AnswersNoContent()
        {
            var id = Guid.NewGuid();
            _service.Setup(s => s.GetByIdAsync(id, _enterpriseId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((Quote?)null);

            Assert.IsType<NoContentResult>(await Controller().GetQuote(id));
        }

        [Fact]
        public async Task GetQuote_AnswersTheQuoteWithItsEffectiveStatus()
        {
            var quote = NewQuote();
            _service.Setup(s => s.GetByIdAsync(quote.Id, _enterpriseId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(quote);

            var result = Assert.IsType<OkObjectResult>(await Controller().GetQuote(quote.Id));

            Assert.Equal(42, Assert.IsType<QuoteDTO>(result.Value).Number);
        }

        /// <summary>
        /// The PDF is not an empty 200. `U3` decided that deliberately: a salesperson who gets a
        /// zero-byte file believes the quote was sent.
        /// </summary>
        [Fact]
        public async Task GetQuotePdf_WhenThereIsNoSuchQuote_AnswersNoContentRatherThanAnEmptyFile()
        {
            var id = Guid.NewGuid();
            _service.Setup(s => s.GetPdfAsync(id, _enterpriseId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(((byte[], long)?)null);

            Assert.IsType<NoContentResult>(await Controller().GetQuotePdf(id));
        }

        [Fact]
        public async Task GetQuotePdf_NamesTheFileAfterTheQuoteNumber()
        {
            var id = Guid.NewGuid();
            _service.Setup(s => s.GetPdfAsync(id, _enterpriseId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(([1, 2, 3], 42L));

            var result = Assert.IsType<FileContentResult>(await Controller().GetQuotePdf(id));

            Assert.Equal("application/pdf", result.ContentType);
            Assert.Equal("orcamento-42.pdf", result.FileDownloadName);
        }

        [Fact]
        public async Task GetQuotePdf_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().GetQuotePdf(Guid.NewGuid()));
        }

        [Fact]
        public async Task AddQuote_CreatesAgainstTheCallersTenant()
        {
            var request = new UpsertQuoteRequestDTO { Items = [] };
            _service.Setup(s => s.CreateAsync(_enterpriseId, request, It.IsAny<CancellationToken>()))
                .ReturnsAsync(NewQuote());

            Assert.IsType<OkObjectResult>(await Controller().AddQuote(request));
            _service.VerifyAll();
        }

        [Fact]
        public async Task AddQuote_WhenTheServiceRefuses_DoesNotEchoTheInnerDetail()
        {
            _service.Setup(s => s.CreateAsync(_enterpriseId, It.IsAny<UpsertQuoteRequestDTO>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InsertDatabaseException(
                    "outer", new Exception("Violation of FK constraint 'FK_Quote_Secret'")));

            var result = Assert.IsType<BadRequestObjectResult>(
                await Controller().AddQuote(new UpsertQuoteRequestDTO { Items = [] }));

            Assert.DoesNotContain("FK_Quote_Secret", result.Value?.ToString());
        }

        [Fact]
        public async Task UpdateQuote_OnAQuoteThatIsNotThisTenants_AnswersNoContent()
        {
            var id = Guid.NewGuid();
            _service.Setup(s => s.UpdateAsync(id, _enterpriseId, It.IsAny<UpsertQuoteRequestDTO>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new NotFoundDatabaseException("no such quote"));

            Assert.IsType<NoContentResult>(
                await Controller().UpdateQuote(id, new UpsertQuoteRequestDTO { Items = [] }));
        }

        [Fact]
        public async Task SetQuoteStatus_ForwardsTheStatusAsTheEnum()
        {
            var quote = NewQuote();
            _service.Setup(s => s.SetStatusAsync(quote.Id, _enterpriseId, EnumQuoteStatus.Accepted, It.IsAny<CancellationToken>()))
                .ReturnsAsync(quote);

            Assert.IsType<OkObjectResult>(await Controller().SetQuoteStatus(
                quote.Id, new QuoteStatusRequestDTO { Status = (int)EnumQuoteStatus.Accepted }));
            _service.VerifyAll();
        }

        [Fact]
        public async Task SetQuoteStatus_OnAQuoteThatIsNotThisTenants_AnswersNoContent()
        {
            var id = Guid.NewGuid();
            _service.Setup(s => s.SetStatusAsync(id, _enterpriseId, It.IsAny<EnumQuoteStatus>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new NotFoundDatabaseException("no such quote"));

            Assert.IsType<NoContentResult>(
                await Controller().SetQuoteStatus(id, new QuoteStatusRequestDTO { Status = (int)EnumQuoteStatus.Sent }));
        }

        /// <summary>
        /// Converting a quote creates an order, so it meets the customer's credit limit. Whether
        /// the caller may push past it is a separate permission, resolved here and passed down —
        /// never assumed.
        /// </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ConvertQuote_PassesDownWhetherTheCallerMayOverrideTheCreditLimit(bool canOverride)
        {
            var quote = NewQuote();
            bool? forwarded = null;

            _service.Setup(s => s.ConvertAsync(
                    quote.Id, _enterpriseId, It.IsAny<ConvertQuoteRequestDTO>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Callback((Guid _, Guid _, ConvertQuoteRequestDTO _, bool allowOverride, CancellationToken _) =>
                    forwarded = allowOverride)
                .ReturnsAsync(quote);

            Assert.IsType<OkObjectResult>(await Controller(canOverride).ConvertQuote(quote.Id, null));
            Assert.Equal(canOverride, forwarded);
        }

        [Fact]
        public async Task ConvertQuote_WhenTheCreditLimitRefuses_AnswersBadRequest()
        {
            var id = Guid.NewGuid();
            _service.Setup(s => s.ConvertAsync(
                    id, _enterpriseId, It.IsAny<ConvertQuoteRequestDTO>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InsertDatabaseException("Customer credit limit exceeded."));

            Assert.IsType<BadRequestObjectResult>(await Controller().ConvertQuote(id, new ConvertQuoteRequestDTO()));
        }

        [Fact]
        public async Task DeleteQuote_ThatIsNotThisTenants_AnswersNoContent()
        {
            var id = Guid.NewGuid();
            _service.Setup(s => s.DeleteAsync(id, _enterpriseId, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new NotFoundDatabaseException("no such quote"));

            Assert.IsType<NoContentResult>(await Controller().DeleteQuote(id));
        }

        [Fact]
        public async Task DeleteQuote_AnswersTheDeletedQuote()
        {
            var quote = NewQuote();
            _service.Setup(s => s.DeleteAsync(quote.Id, _enterpriseId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(quote);

            Assert.IsType<OkObjectResult>(await Controller().DeleteQuote(quote.Id));
        }

        [Fact]
        public async Task DeleteQuote_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().DeleteQuote(Guid.NewGuid()));
        }
    }

    public class OpportunitiesControllerTests
    {
        private readonly Guid _enterpriseId = Guid.NewGuid();
        private readonly Guid _userId = Guid.NewGuid();
        private readonly Mock<IOpportunityRadarService> _service = new();
        private readonly Mock<IRepository<User>> _userRepository = new();

        private OpportunitiesController Controller() =>
            new OpportunitiesController(_service.Object, _userRepository.Object).WithTenant(_enterpriseId, _userId);

        private OpportunitiesController Anonymous() =>
            new OpportunitiesController(_service.Object, _userRepository.Object).WithoutTenant();

        [Fact]
        public async Task GetOpportunities_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().GetOpportunities(new OpportunityFilterDTO()));
        }

        [Fact]
        public async Task GetOpportunities_ScopesToTheCallersTenant()
        {
            var filter = new OpportunityFilterDTO();
            _service.Setup(s => s.GetOpportunitiesAsync(_enterpriseId, filter))
                .ReturnsAsync(new PaginatedResultDTO<OpportunityDTO>());

            Assert.IsType<OkObjectResult>(await Controller().GetOpportunities(filter));
            _service.VerifyAll();
        }

        [Fact]
        public async Task GetOpportunity_ThatIsNotThisTenants_AnswersNoContent()
        {
            var id = Guid.NewGuid();
            _service.Setup(s => s.GetOpportunityByIdAsync(id, _enterpriseId)).ReturnsAsync((OpportunityDTO?)null);

            Assert.IsType<NoContentResult>(await Controller().GetOpportunity(id));
        }

        [Fact]
        public async Task GetOpportunity_AnswersTheRow()
        {
            var id = Guid.NewGuid();
            _service.Setup(s => s.GetOpportunityByIdAsync(id, _enterpriseId))
                .ReturnsAsync(new OpportunityDTO { Id = id });

            var result = Assert.IsType<OkObjectResult>(await Controller().GetOpportunity(id));
            Assert.Equal(id, Assert.IsType<OpportunityDTO>(result.Value).Id);
        }

        /// <summary>
        /// Feedback is attributed to the caller, so the user claim is read and passed down rather
        /// than taken from the body where a client could choose it.
        /// </summary>
        [Fact]
        public async Task AddFeedback_AttributesItToTheCallerFromTheToken()
        {
            var id = Guid.NewGuid();
            Guid? attributedTo = null;

            _service.Setup(s => s.AddFeedbackAsync(
                    _enterpriseId, id, It.IsAny<Guid?>(), It.IsAny<OpportunityFeedbackRequestDTO>(), It.IsAny<bool>()))
                .Callback((Guid _, Guid _, Guid? userId, OpportunityFeedbackRequestDTO _, bool _) =>
                    attributedTo = userId)
                .ReturnsAsync(new OpportunityFeedbackDTO());

            Assert.IsType<OkObjectResult>(
                await Controller().AddFeedback(id, new OpportunityFeedbackRequestDTO { Status = "done" }));

            Assert.Equal(_userId, attributedTo);
        }

        [Fact]
        public async Task AddFeedback_OnAnOpportunityThatIsNotThisTenants_AnswersNoContent()
        {
            var id = Guid.NewGuid();
            _service.Setup(s => s.AddFeedbackAsync(
                    _enterpriseId, id, It.IsAny<Guid?>(), It.IsAny<OpportunityFeedbackRequestDTO>(), It.IsAny<bool>()))
                .ThrowsAsync(new NotFoundDatabaseException("no such opportunity"));

            Assert.IsType<NoContentResult>(
                await Controller().AddFeedback(id, new OpportunityFeedbackRequestDTO { Status = "done" }));
        }

        [Fact]
        public async Task AddFeedback_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(
                await Anonymous().AddFeedback(Guid.NewGuid(), new OpportunityFeedbackRequestDTO()));
        }

        /// <summary>
        /// The trigger type is what separates this from the scheduled sweep in the run log, so a
        /// manual recompute must say so.
        /// </summary>
        [Fact]
        public async Task Recompute_RecordsItAsAManualRun()
        {
            string? trigger = null;
            _service.Setup(s => s.RecomputeAsync(
                    _enterpriseId, It.IsAny<Guid?>(), It.IsAny<OpportunityRecomputeRequestDTO>(), It.IsAny<string>()))
                .Callback((Guid _, Guid? _, OpportunityRecomputeRequestDTO _, string triggerType) =>
                    trigger = triggerType)
                .ReturnsAsync(new OpportunityRunLogDTO());

            Assert.IsType<OkObjectResult>(await Controller().Recompute(new OpportunityRecomputeRequestDTO()));
            Assert.Equal("manual", trigger);
        }

        [Fact]
        public async Task Recompute_WhenItIsBlocked_AnswersBadRequest()
        {
            _service.Setup(s => s.RecomputeAsync(
                    _enterpriseId, It.IsAny<Guid?>(), It.IsAny<OpportunityRecomputeRequestDTO>(), It.IsAny<string>()))
                .ThrowsAsync(new InsertDatabaseException("A recompute is already running."));

            Assert.IsType<BadRequestObjectResult>(await Controller().Recompute(new OpportunityRecomputeRequestDTO()));
        }

        [Fact]
        public async Task Recompute_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().Recompute(new OpportunityRecomputeRequestDTO()));
        }

        [Fact]
        public async Task GetSummary_AnswersTheTenantsSummary()
        {
            _service.Setup(s => s.GetSummaryAsync(_enterpriseId)).ReturnsAsync(new OpportunitySummaryDTO());

            Assert.IsType<OkObjectResult>(await Controller().GetSummary());
            _service.VerifyAll();
        }

        [Fact]
        public async Task GetSummary_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().GetSummary());
        }
    }
}
