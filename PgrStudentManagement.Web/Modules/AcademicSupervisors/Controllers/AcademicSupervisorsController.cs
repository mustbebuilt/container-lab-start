using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PgrStudentManagement.Web.Modules.AcademicSupervisors.Models;
using PgrStudentManagement.Web.Modules.AcademicSupervisors.Services;

namespace PgrStudentManagement.Web.Modules.AcademicSupervisors.Controllers;

[Authorize]
public class AcademicSupervisorsController : Controller
{
    private readonly AcademicSupervisorService _academicSupervisorService;

    public AcademicSupervisorsController(
        AcademicSupervisorService academicSupervisorService
    )
    {
        _academicSupervisorService = academicSupervisorService;
    }

    public async Task<IActionResult> Index(
        CancellationToken cancellationToken = default)
    {
        var supervisors =
            await _academicSupervisorService
                .GetAcademicSupervisorsAsync(cancellationToken);

        return View(supervisors);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new CreateAcademicSupervisorViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        CreateAcademicSupervisorViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var supervisor = new AcademicSupervisor
        {
            StaffNumber = model.StaffNumber.Trim(),
            FirstName = model.FirstName.Trim(),
            LastName = model.LastName.Trim(),
            EmailAddress = model.EmailAddress.Trim(),
            EmploymentType =
                model.EmploymentType!.Value,
            TrainingCompleted =
                model.TrainingCompleted,
            EligibleRoleEntries =
                model.EligibleRoles.Select(role =>
                    new AcademicSupervisorEligibleRole
                    {
                        SupervisorStaffNumber = model.StaffNumber.Trim(),
                        Role = role
                    })
                .ToList()
        };

        var outcome = await
            _academicSupervisorService
                .CreateAcademicSupervisorAsync(
                    supervisor, cancellationToken);

        switch (outcome)
        {
            case SupervisorOutcome.Created:
                TempData["SuccessMessage"] =
                    "Academic Supervisor created successfully.";

                return RedirectToAction(
                    nameof(Details),
                    new
                    {
                        staffNumber =
                            supervisor.StaffNumber
                    });

            case SupervisorOutcome.DuplicateStaffNumber:
                ModelState.AddModelError(
                    nameof(model.StaffNumber),
                    "An Academic Supervisor with this "
                    + "staff number already exists.");

                return View(model);

            case SupervisorOutcome
                .MissingRequiredInformation:
                ModelState.AddModelError(
                    string.Empty,
                    "The Academic Supervisor could not "
                    + "be created because required "
                    + "information is missing.");

                return View(model);

            default:
                ModelState.AddModelError(
                    string.Empty,
                    "The Academic Supervisor could not "
                    + "be created.");

                return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Details(string staffNumber,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(staffNumber))
        {
            return View("NotFound");
        }

        var supervisor = await 
            _academicSupervisorService
                .GetAcademicSupervisorAsync(
                    staffNumber, cancellationToken);

        if (supervisor is null)
        {
            return View("NotFound");
        }

        return View(supervisor);
    }

    [HttpGet]
    public async Task<IActionResult> Assign(
        string studentNumber,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(studentNumber))
        {
            return View("StudentNotFound");
        }

        var result =
            await _academicSupervisorService
                .PrepareAssignmentAsync(
                    studentNumber,
                    cancellationToken);

        if (result.Student is null)
        {
            return View("StudentNotFound");
        }

        return View(result);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(
        AssignmentRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var invalidForm =
                await _academicSupervisorService
                    .PrepareAssignmentAsync(
                        request.StudentNumber,
                        cancellationToken);

            if (invalidForm.Student is null)
            {
                return View("StudentNotFound");
            }

            PreserveAssignmentValues(request);

            return View(invalidForm);
        }

        var outcome =
            await _academicSupervisorService
                .AssignSupervisorAsync(
                    request,
                    cancellationToken);

        if (outcome == AssignmentOutcome.Created)
        {
            TempData["SuccessMessage"] =
                "Academic Supervisor assigned successfully.";

            return RedirectToAction(
                nameof(Team),
                new
                {
                    studentNumber =
                        request.StudentNumber
                });
        }

        AddAssignmentError(outcome);

        var form =
            await _academicSupervisorService
                .PrepareAssignmentAsync(
                    request.StudentNumber,
                    cancellationToken);

        if (form.Student is null)
        {
            return View("StudentNotFound");
        }

        PreserveAssignmentValues(request);

        return View(form);
    }

    [HttpGet]
    public async Task<IActionResult> Team(
        string studentNumber,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(studentNumber))
        {
            return View(new SupervisoryTeamResult
            {
                StudentFound = false
            });
        }

        var result =
            await _academicSupervisorService
                .GetSupervisoryTeamAsync(
                    studentNumber,
                    cancellationToken);

        if (!result.StudentFound)
        {
            return View("StudentNotFound");
        }

        return View(result);
    }

    private void AddAssignmentError(AssignmentOutcome outcome)
    {
        var message = outcome switch
        {
            AssignmentOutcome.StudentNotFound =>
                "Student not found.",

            AssignmentOutcome.SupervisorNotFound =>
                "Academic Supervisor not found.",

            AssignmentOutcome.DuplicateAssignment =>
                "This current supervisory assignment "
                + "already exists.",

            AssignmentOutcome.RoleAlreadyFilled =>
                "The selected supervisory role is "
                + "already filled for this student.",

            AssignmentOutcome.SupervisorNotEligible =>
                "The selected Academic Supervisor is "
                + "not eligible for this role.",

            AssignmentOutcome
                .EmploymentTypeNotPermitted =>
                "The supervisor's employment type is "
                + "not permitted for the selected role.",

            AssignmentOutcome.ExceptionRequired =>
                "An authorised exception is required "
                + "before a fourth supervisor can be "
                + "assigned.",

            _ =>
                "The supervisory assignment could "
                + "not be created."
        };

        ModelState.AddModelError(
            string.Empty,
            message);
    }

    private void PreserveAssignmentValues(AssignmentRequest request)
    {
        ViewData["SelectedStaffNumber"] =
            request.StaffNumber;

        ViewData["SelectedRole"] =
            request.Role;

        ViewData["SelectedStartDate"] =
            request.StartDate.ToString(
                "yyyy-MM-dd");

        ViewData["ExceptionAuthorised"] =
            request.ExceptionAuthorised;
    }
}