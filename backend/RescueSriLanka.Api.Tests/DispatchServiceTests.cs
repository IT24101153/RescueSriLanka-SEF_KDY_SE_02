using RescueSriLanka.Api.Agents.SafetyValidation;
using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Services;
using Xunit;

namespace RescueSriLanka.Api.Tests
{
    public class DispatchServiceTests
    {
        private static async Task<(Data.ComponentDDbContext Db, Assignment Assignment)> SeedAssignmentAsync()
        {
            var db = TestDbFactory.Create();

            var team = new RescueTeam { Name = "Delta", Status = TeamStatus.Available };
            team.Members.Add(new TeamMember { FullName = "X", Phone = "1", Skill = SkillType.StructuralCollapse, IsAvailable = true });
            var vehicle = new Vehicle { PlateNumber = "XYZ-1", Type = VehicleType.Truck, Status = VehicleStatus.Available, Capacity = 4 };
            team.Vehicles.Add(vehicle);
            db.RescueTeams.Add(team);
            await db.SaveChangesAsync();

            var assignment = new Assignment
            {
                RescueTeamId = team.Id,
                VehicleId = vehicle.Id,
                RequiredSkill = SkillType.StructuralCollapse
            };
            db.Assignments.Add(assignment);
            await db.SaveChangesAsync();

            return (db, assignment);
        }

        [Fact]
        public async Task CannotTransitionOutOfPendingWithoutApproval()
        {
            var (db, assignment) = await SeedAssignmentAsync();
            var agent = new SafetyValidationAgent(db);
            var service = new DispatchService(db, agent);

            var (dispatch, _, createError) = await service.CreateAsync(new CreateDispatchDto(assignment.Id, null));
            Assert.NotNull(dispatch);
            Assert.Null(createError);

            var (success, error, _) = await service.TransitionStatusAsync(
                dispatch!.Id, new TransitionDispatchStatusDto(DispatchStatus.Dispatched, null));

            Assert.False(success);
            Assert.Contains("approved", error);
        }

        [Fact]
        public async Task CanTransitionOutOfPendingAfterApproval()
        {
            var (db, assignment) = await SeedAssignmentAsync();
            var agent = new SafetyValidationAgent(db);
            var service = new DispatchService(db, agent);

            var (dispatch, _, _) = await service.CreateAsync(new CreateDispatchDto(assignment.Id, null));
            await service.ApproveAsync(dispatch!.Id, "coordinator-1", new ApproveDispatchDto(true, null));

            var (success, error, updated) = await service.TransitionStatusAsync(
                dispatch.Id, new TransitionDispatchStatusDto(DispatchStatus.Dispatched, null));

            Assert.True(success, error);
            Assert.Equal(DispatchStatus.Dispatched, updated!.Status);
            Assert.NotNull(updated.DispatchedAt);
        }

        [Fact]
        public async Task CannotSkipStatesInTheWorkflow()
        {
            var (db, assignment) = await SeedAssignmentAsync();
            var agent = new SafetyValidationAgent(db);
            var service = new DispatchService(db, agent);

            var (dispatch, _, _) = await service.CreateAsync(new CreateDispatchDto(assignment.Id, null));
            await service.ApproveAsync(dispatch!.Id, "coordinator-1", new ApproveDispatchDto(true, null));

            var (success, error, _) = await service.TransitionStatusAsync(
                dispatch.Id, new TransitionDispatchStatusDto(DispatchStatus.OnScene, null));

            Assert.False(success);
            Assert.Contains("Cannot transition", error);
        }

        [Fact]
        public async Task RejectedDispatchIsCancelledAndCannotProgress()
        {
            var (db, assignment) = await SeedAssignmentAsync();
            var agent = new SafetyValidationAgent(db);
            var service = new DispatchService(db, agent);

            var (dispatch, _, _) = await service.CreateAsync(new CreateDispatchDto(assignment.Id, null));
            var afterReject = await service.ApproveAsync(dispatch!.Id, "coordinator-1", new ApproveDispatchDto(false, "Not safe"));

            Assert.Equal(DispatchStatus.Cancelled, afterReject!.Status);
            Assert.Equal(ApprovalStatus.Rejected, afterReject.ApprovalStatus);

            var (success, _, _) = await service.TransitionStatusAsync(
                dispatch.Id, new TransitionDispatchStatusDto(DispatchStatus.Dispatched, null));

            Assert.False(success); // Cancelled is terminal — no further status transitions allowed
        }

        [Fact]
        public async Task FullHappyPathReachesResolved()
        {
            var (db, assignment) = await SeedAssignmentAsync();
            var agent = new SafetyValidationAgent(db);
            var service = new DispatchService(db, agent);

            var (dispatch, _, _) = await service.CreateAsync(new CreateDispatchDto(assignment.Id, null));
            await service.ApproveAsync(dispatch!.Id, "coordinator-1", new ApproveDispatchDto(true, null));

            foreach (var status in new[] { DispatchStatus.Dispatched, DispatchStatus.EnRoute, DispatchStatus.OnScene, DispatchStatus.Resolved })
            {
                var (success, error, _) = await service.TransitionStatusAsync(
                    dispatch.Id, new TransitionDispatchStatusDto(status, null));
                Assert.True(success, $"Failed moving to {status}: {error}");
            }

            var final = await service.GetByIdAsync(dispatch.Id);
            Assert.Equal(DispatchStatus.Resolved, final!.Status);
            Assert.NotNull(final.ResolvedAt);
        }

        // --- Tests for the re-dispatch-after-cancellation fix ---

        [Fact]
        public async Task CanCreateNewDispatchAfterPreviousOneWasRejectedCancelled()
        {
            var (db, assignment) = await SeedAssignmentAsync();
            var agent = new SafetyValidationAgent(db);
            var service = new DispatchService(db, agent);

            var (firstDispatch, _, _) = await service.CreateAsync(new CreateDispatchDto(assignment.Id, null));
            await service.ApproveAsync(firstDispatch!.Id, "coordinator-1", new ApproveDispatchDto(false, "Team reassigned"));

            // This is the exact scenario that used to be permanently blocked:
            // a rejected/cancelled dispatch previously prevented any future
            // dispatch from ever being created for the same assignment.
            var (secondDispatch, validation, error) = await service.CreateAsync(new CreateDispatchDto(assignment.Id, null));

            Assert.Null(error);
            Assert.NotNull(secondDispatch);
            Assert.NotEqual(firstDispatch.Id, secondDispatch!.Id);
            Assert.Equal(DispatchStatus.Pending, secondDispatch.Status);
            Assert.True(validation.Passed);
        }

        [Fact]
        public async Task CannotCreateSecondDispatchWhileFirstIsStillActive()
        {
            var (db, assignment) = await SeedAssignmentAsync();
            var agent = new SafetyValidationAgent(db);
            var service = new DispatchService(db, agent);

            await service.CreateAsync(new CreateDispatchDto(assignment.Id, null));

            // First dispatch is still Pending (never rejected/cancelled) —
            // a second one must be blocked.
            var (secondDispatch, _, error) = await service.CreateAsync(new CreateDispatchDto(assignment.Id, null));

            Assert.Null(secondDispatch);
            Assert.Contains("active dispatch", error);
        }

        [Fact]
        public async Task CannotCreateSecondDispatchAfterFirstIsResolved()
        {
            var (db, assignment) = await SeedAssignmentAsync();
            var agent = new SafetyValidationAgent(db);
            var service = new DispatchService(db, agent);

            var (dispatch, _, _) = await service.CreateAsync(new CreateDispatchDto(assignment.Id, null));
            await service.ApproveAsync(dispatch!.Id, "coordinator-1", new ApproveDispatchDto(true, null));
            foreach (var status in new[] { DispatchStatus.Dispatched, DispatchStatus.EnRoute, DispatchStatus.OnScene, DispatchStatus.Resolved })
            {
                await service.TransitionStatusAsync(dispatch.Id, new TransitionDispatchStatusDto(status, null));
            }

            var (secondDispatch, _, error) = await service.CreateAsync(new CreateDispatchDto(assignment.Id, null));

            Assert.Null(secondDispatch);
            Assert.Contains("already been resolved", error);
        }

        [Fact]
        public async Task CreatingDispatchForUnknownAssignmentReturnsError()
        {
            var db = TestDbFactory.Create();
            var agent = new SafetyValidationAgent(db);
            var service = new DispatchService(db, agent);

            var (dispatch, _, error) = await service.CreateAsync(new CreateDispatchDto(Guid.NewGuid(), null));

            Assert.Null(dispatch);
            Assert.Equal("Assignment not found.", error);
        }

        [Fact]
        public async Task FailedSafetyValidationDoesNotCreateDispatch()
        {
            var (db, assignment) = await SeedAssignmentAsync();
            var service = new DispatchService(db, new FailingSafetyValidationAgent());

            var (dispatch, validation, error) = await service.CreateAsync(new CreateDispatchDto(assignment.Id, null));

            Assert.Null(dispatch);
            Assert.False(validation.Passed);
            Assert.Contains("Safety validation failed", error);
            Assert.Empty(db.Dispatches);
        }

        [Fact]
        public async Task ResolvedDispatchCannotTransitionAgain()
        {
            var (db, assignment) = await SeedAssignmentAsync();
            var service = new DispatchService(db, new SafetyValidationAgent(db));

            var (dispatch, _, _) = await service.CreateAsync(new CreateDispatchDto(assignment.Id, null));
            await service.ApproveAsync(dispatch!.Id, "coordinator-1", new ApproveDispatchDto(true, null));
            foreach (var status in new[] { DispatchStatus.Dispatched, DispatchStatus.EnRoute, DispatchStatus.OnScene, DispatchStatus.Resolved })
                await service.TransitionStatusAsync(dispatch.Id, new TransitionDispatchStatusDto(status, null));

            var (success, error, _) = await service.TransitionStatusAsync(
                dispatch.Id, new TransitionDispatchStatusDto(DispatchStatus.EnRoute, null));

            Assert.False(success);
            Assert.Contains("Cannot transition", error);
        }

        private sealed class FailingSafetyValidationAgent : ISafetyValidationAgent
        {
            public Task<SafetyValidationResultDto> ValidateAsync(Guid assignmentId) =>
                Task.FromResult(new SafetyValidationResultDto(false, new List<string> { "Safety check failed." }, DateTime.UtcNow));
        }
    }
}
