using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RescueSriLanka.Api.Features.ComponentD.Controllers;
using RescueSriLanka.Api.Features.ComponentD.DTOs;
using RescueSriLanka.Api.Features.ComponentD.Models;
using RescueSriLanka.Api.Features.ComponentD.Services;
using Xunit;

namespace RescueSriLanka.Api.Tests
{
    public class DispatchesControllerTests
    {
        [Fact]
        public async Task LegacyApproveCannotBypassSafeDecisionWorkflow()
        {
            var service = new CapturingDispatchService();
            var controller = CreateController(service, new Claim(ClaimTypes.NameIdentifier, "jwt-coordinator"));

            var result = await controller.Approve(Guid.NewGuid(), new ApproveDispatchDto(true, null));

            Assert.Null(service.ApprovedByUserId);
            Assert.IsType<ConflictObjectResult>(result.Result);
        }

        [Fact]
        public async Task LegacyApproveIsDisabledEvenWhenIdentityIsMissing()
        {
            var controller = CreateController(new CapturingDispatchService());

            var result = await controller.Approve(Guid.NewGuid(), new ApproveDispatchDto(true, null));

            Assert.IsType<ConflictObjectResult>(result.Result);
        }

        private static DispatchesController CreateController(CapturingDispatchService service, params Claim[] claims)
        {
            var identity = new ClaimsIdentity(claims, claims.Length > 0 ? "test" : null);
            return new DispatchesController(service)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
                }
            };
        }

        private sealed class CapturingDispatchService : IDispatchService
        {
            public string? ApprovedByUserId { get; private set; }

            public Task<DispatchDto?> ApproveAsync(Guid dispatchId, string approvedByUserId, ApproveDispatchDto dto)
            {
                ApprovedByUserId = approvedByUserId;
                return Task.FromResult<DispatchDto?>(new DispatchDto(
                    dispatchId, Guid.NewGuid(), DispatchStatus.Pending, ApprovalStatus.Approved,
                    approvedByUserId, DateTime.UtcNow, null, null, null, null, null, dto.Notes));
            }

            public Task<(DispatchDto? Dispatch, SafetyValidationResultDto Validation, string? Error)> CreateAsync(CreateDispatchDto dto) => throw new NotImplementedException();
            public Task<List<DispatchDto>> GetAllAsync() => throw new NotImplementedException();
            public Task<DispatchDto?> GetByIdAsync(Guid id) => throw new NotImplementedException();
            public Task<CoordinatorDecisionResultDto> DecideAsync(Guid assignmentId, string coordinatorId, CoordinatorDecisionDto dto) => throw new NotImplementedException();
            public Task<(bool Success, string? Error, DispatchDto? Dispatch)> TransitionStatusAsync(Guid dispatchId, TransitionDispatchStatusDto dto) => throw new NotImplementedException();
        }
    }
}
