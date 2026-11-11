using Microsoft.EntityFrameworkCore;

using PgrStudentManagement.Web.Modules.AcademicSupervisors.Models;
using PgrStudentManagement.Web.Modules.PhdApplications.Models;
using PgrStudentManagement.Web.Modules.Students.Models;

namespace PgrStudentManagement.Web.Modules.Students.Data;

public class StudentManagementDbContext : DbContext
{
    public StudentManagementDbContext(
        DbContextOptions<StudentManagementDbContext> options)
        : base(options)
    {
    }

    public DbSet<Student> Students => Set<Student>();
    public DbSet<Enrolment> Enrolments => Set<Enrolment>();

    // Tables for academic supervisor module
    public DbSet<AcademicSupervisor> AcademicSupervisors =>
        Set<AcademicSupervisor>();

    public DbSet<AcademicSupervisorEligibleRole>
        AcademicSupervisorEligibleRoles =>
            Set<AcademicSupervisorEligibleRole>();

    public DbSet<SupervisoryAssignment> SupervisoryAssignments =>
        Set<SupervisoryAssignment>();

    // Tables for PhD applications module
    public DbSet<PhdApplication> PhdApplications =>
        Set<PhdApplication>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureStudents(modelBuilder);
        ConfigureAcademicSupervisors(modelBuilder);
        ConfigurePhdApplications(modelBuilder);
    }

    private static void ConfigureStudents(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Student>(entity =>
        {
            entity.HasKey(student =>
                student.StudentNumber);

            entity.Property(student => student.StudentNumber)
                .HasMaxLength(20);

            entity.Property(student => student.FirstName)
                .HasMaxLength(100);

            entity.Property(student => student.LastName)
                .HasMaxLength(100);

            entity.Property(student => student.EmailAddress)
                .HasMaxLength(255);
        });

        modelBuilder.Entity<Enrolment>(entity =>
        {
            entity.HasKey(enrolment => enrolment.EnrolmentId);

            entity.Property(enrolment => enrolment.StudentNumber)
                .HasMaxLength(20);

            entity.Property(enrolment => enrolment.ProgrammeCode)
                .HasMaxLength(20);

            entity.Property(enrolment => enrolment.ModeOfStudy)
                .HasMaxLength(20);

            entity
            .HasOne(enrolment => enrolment.Student)
            .WithMany(student => student.Enrolments)
            .HasForeignKey(enrolment => enrolment.StudentNumber)
            .OnDelete(DeleteBehavior.Restrict);

            entity
            .Property(enrolment => enrolment.Status)
            .HasConversion<string>()
            .HasMaxLength(30);
        });
    }

    private static void ConfigureAcademicSupervisors(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AcademicSupervisor>(
            entity =>
            {
                entity.ToTable("AcademicSupervisors");
                entity.HasKey(supervisor => supervisor.StaffNumber);
                entity.Property(supervisor => supervisor.StaffNumber)
                    .IsRequired()
                    .HasMaxLength(20);

                entity.Property(supervisor => supervisor.FirstName)
                    .HasMaxLength(100);
                entity.Property(supervisor => supervisor.LastName)
                    .HasMaxLength(100);

                entity.Property(supervisor => supervisor.EmailAddress)
                    .IsRequired()
                    .HasMaxLength(255);

                entity.HasMany(supervisor => supervisor.EligibleRoleEntries)
                    .WithOne(eligibleRole =>
                        eligibleRole.AcademicSupervisor)
                    .HasForeignKey(eligibleRole =>
                        eligibleRole.SupervisorStaffNumber)
                    .OnDelete(DeleteBehavior.Cascade);

            });

        modelBuilder.Entity<AcademicSupervisorEligibleRole>(
            entity =>
            {
                entity.ToTable("AcademicSupervisorEligibleRoles");

                entity.HasKey(eligibleRole => new
                {
                    eligibleRole.SupervisorStaffNumber,
                    eligibleRole.Role
                });

                entity.Property(eligibleRole => eligibleRole.SupervisorStaffNumber)
                    .HasMaxLength(20);

                entity.Property(eligibleRole => eligibleRole.Role)
                    .HasConversion<string>()
                    .HasMaxLength(50)
                    .IsRequired();
            });

        modelBuilder.Entity<SupervisoryAssignment>(
            entity =>
            {
                entity.ToTable("SupervisoryAssignments");

                entity.HasKey(assignment => new
                {
                    assignment.StudentNumber,
                    assignment.SupervisorStaffNumber,
                    assignment.Role,
                    assignment.StartDate
                });

                entity.Property(assignment => assignment.StudentNumber)
                    .HasMaxLength(20);

                entity.Property(assignment => assignment.SupervisorStaffNumber)
                    .HasMaxLength(20);

                entity.Property(assignment => assignment.Role)
                    .HasConversion<string>()
                    .HasMaxLength(50);

                entity.HasOne<Student>()
                    .WithMany()
                    .HasForeignKey(assignment => assignment.StudentNumber)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne<AcademicSupervisor>()
                    .WithMany()
                    .HasForeignKey(assignment =>
                        assignment.SupervisorStaffNumber)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.Ignore(assignment => assignment.Current);
            });
    }

    private static void ConfigurePhdApplications(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PhdApplication>(
            entity =>
            {
                entity.ToTable("PhdApplications");

                entity.HasKey(application =>
                    application.ApplicationReference);

                entity.Property(application =>
                        application.ApplicationReference)
                    .IsRequired()
                    .HasMaxLength(20);

                entity.Property(application => application.FirstName)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(application => application.LastName)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(application => application.EmailAddress)
                    .IsRequired()
                    .HasMaxLength(255);

                entity.Property(application => application.ProgrammeCode)
                    .IsRequired()
                    .HasMaxLength(20);

                entity.Property(application => application.ModeOfStudy)
                    .IsRequired()
                    .HasMaxLength(20);

                entity.Property(application => application.ProposedThesisTitle)
                    .IsRequired()
                    .HasMaxLength(300);

                entity.Property(application => application.ResearchSummary)
                    .HasMaxLength(2000);

                entity.Property(application =>
                        application.ProposedSupervisorStaffNumber)
                    .HasMaxLength(20);

                entity.Property(application =>
                        application.EnrolledStudentNumber)
                    .HasMaxLength(20);

                entity.Property(application => application.FundingSource)
                    .HasConversion<string>()
                    .HasMaxLength(30)
                    .IsRequired();

                entity.Property(application => application.Status)
                    .HasConversion<string>()
                    .HasMaxLength(30)
                    .IsRequired();

                entity.HasOne<AcademicSupervisor>()
                    .WithMany()
                    .HasForeignKey(application =>
                        application.ProposedSupervisorStaffNumber)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne<Student>()
                    .WithMany()
                    .HasForeignKey(application =>
                        application.EnrolledStudentNumber)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.Restrict);
            });
    }
}
