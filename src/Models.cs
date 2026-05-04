namespace MntParser;

public interface IDateRangeTarget
{
  string? StartDate { get; set; }
  string? EndDate { get; set; }
  string? DurationText { get; set; }
}

public sealed class ExtractedApplicationDocument
{
  public ApplicantRecord Applicant { get; init; } = new();
  public List<EmploymentRecord> EmploymentHistory { get; init; } = [];
  public EmploymentGapSection EmploymentGaps { get; init; } = new();
  public List<UniversityDegreeRecord> UniversityDegrees { get; init; } = [];
  public List<SecondaryEducationRecord> SecondaryEducationHistory { get; init; } = [];
  public List<TrainingRecord> TrainingAndCpd { get; init; } = [];
  public List<ProfessionalBodyMembershipRecord> ProfessionalBodiesMembership { get; init; } = [];
  public string? SupportingStatement { get; init; }
  public bool? PartTimeOrJobShare { get; init; }
  public TeacherStatusRecord TeacherStatus { get; init; } = new();
  public AdditionalInformationRecord AdditionalInformation { get; init; } = new();
}

public sealed class ApplicantRecord
{
  public string? Title { get; set; }
  public string? FirstName { get; set; }
  public string? LastName { get; set; }
  public string? PreferredName { get; set; }
  public bool? RequiresUkSponsorship { get; set; }
}

public sealed class EmploymentRecord : IDateRangeTarget
{
  public string? Employer { get; set; }
  public string? JobTitle { get; set; }
  public string? MainDuties { get; set; }
  public string? ReasonForLeaving { get; set; }
  public string? SalaryText { get; set; }
  public string? StartDate { get; set; }
  public string? EndDate { get; set; }
  public string? DurationText { get; set; }
}

public sealed class EmploymentGapSection
{
  public List<EmploymentGap> Gaps { get; init; } = [];
  public string? AdditionalReasons { get; set; }
}

public sealed class EmploymentGap : IDateRangeTarget
{
  public string? StartDate { get; set; }
  public string? EndDate { get; set; }
  public string? DurationText { get; set; }
  public string? BetweenDescription { get; set; }
  public string? Reason { get; set; }
}

public sealed class UniversityDegreeRecord : IDateRangeTarget
{
  public string? Institution { get; set; }
  public string? Course { get; set; }
  public string? Qualification { get; set; }
  public string? Grade { get; set; }
  public string? StartDate { get; set; }
  public string? EndDate { get; set; }
  public string? DurationText { get; set; }
}

public sealed class SecondaryEducationRecord : IDateRangeTarget
{
  public string? SchoolCollege { get; set; }
  public IReadOnlyList<AcademicResultRecord> GcsesOrEquivalent { get; set; } = [];
  public IReadOnlyList<AcademicResultRecord> ALevelsOrEquivalent { get; set; } = [];
  public string? Other { get; set; }
  public string? StartDate { get; set; }
  public string? EndDate { get; set; }
  public string? DurationText { get; set; }
}

public sealed class AcademicResultRecord
{
  public string? Subject { get; init; }
  public string? Grade { get; init; }
}

public sealed class TrainingRecord
{
  public string? Title { get; set; }
  public string? OrganisingBody { get; set; }
  public string? Qualification { get; set; }
  public string? Date { get; set; }
}

public sealed class ProfessionalBodyMembershipRecord
{
  public string? ProfessionalBody { get; set; }
  public string? MembershipLevel { get; set; }
  public string? Date { get; set; }
}

public sealed class TeacherStatusRecord
{
  public string? QualifiedTeacherStatus { get; set; }
  public string? TeacherReferenceNumber { get; set; }
  public string? DateAttained { get; set; }
  public string? ExpectedDateOfCompletion { get; set; }
  public bool? CompletedStatutoryInduction { get; set; }
  public string? OverseasQualificationRecognised { get; set; }
  public bool? SubjectToTeacherProhibitionOrder { get; set; }
  public bool? SubjectToTeachingCouncilSanction { get; set; }
}

public sealed class AdditionalInformationRecord
{
  public bool? RelatedToOrganisation { get; set; }
  public bool? PreviouslyEmployedByOrganisation { get; set; }
  public bool? HoldsOtherAppointment { get; set; }
  public bool? RequiresNotice { get; set; }
}

sealed class PdfPageContent
{
  public int Number { get; init; }
  public string ReadOrderText { get; init; } = string.Empty;
  public string ContentText { get; init; } = string.Empty;
  public IReadOnlyList<DocumentLine> Lines { get; init; } = [];
}

sealed class DocumentLine
{
  public int PageNumber { get; init; }
  public double Y { get; init; }
  public string Text { get; init; } = string.Empty;
}

sealed class PositionedWord(string text, double y, double x)
{
  public string Text { get; } = text;
  public double X { get; } = x;
  public double Y { get; } = y;
}
