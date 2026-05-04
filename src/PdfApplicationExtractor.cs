using System.Text.RegularExpressions;

namespace MntParser;

static partial class PdfApplicationExtractor
{
  public static ExtractedApplicationDocument Extract(IReadOnlyList<PdfPageContent> pages)
  {
    var allLines = pages.SelectMany(page => page.Lines)
        .Where(line => !IsDecorativeLine(line.Text))
        .ToList();

    var contentText = string.Join("\n\n", pages.Select(page => page.ContentText));

    var employmentHistoryLines = GetLinesBetween(allLines, "2. EMPLOYMENT HISTORY", "GAPS IN EMPLOYMENT HISTORY");
    var employmentGapLines = GetLinesBetween(allLines, "GAPS IN EMPLOYMENT HISTORY", "3. UNIVERSITY DEGREES & DIPLOMAS");
    var universityDegreeLines = GetLinesBetween(allLines, "3. UNIVERSITY DEGREES & DIPLOMAS", "4. SECONDARY/FURTHER EDUCATION");
    var secondaryEducationLines = GetLinesBetween(allLines, "4. SECONDARY/FURTHER EDUCATION", "5. TRAINING & CONTINUED PROFESSIONAL DEVELOPMENT (CPD)");
    var trainingLines = GetLinesBetween(allLines, "5. TRAINING & CONTINUED PROFESSIONAL DEVELOPMENT (CPD)", "6. PROFESSIONAL BODIES MEMBERSHIP");
    var membershipLines = GetLinesBetween(allLines, "6. PROFESSIONAL BODIES MEMBERSHIP", "7. SUPPORTING STATEMENT");
    var supportingStatementLines = GetLinesBetween(allLines, "7. SUPPORTING STATEMENT", "8. REFEREES");
    var flexibleWorkingText = ExtractBetween(contentText, "FLEXIBLE WORKING", "Part 2: Personal Information & Declaration");
    var applicantText = ExtractBetween(contentText, "9. PERSONAL DETAILS", "TEACHER STATUS");
    var teacherStatusText = ExtractBetween(contentText, "TEACHER STATUS", "DISABILITY AND ACCESSIBILITY");
    var additionalInformationText = ExtractBetween(contentText, "10. ADDITIONAL INFORMATION", "DISCLOSURE AND BARRING AND CHILDCARE DISQUALIFICATION");

    return new ExtractedApplicationDocument
    {
      Applicant = ParseApplicant(applicantText),
      EmploymentHistory = ParseEmploymentHistory(employmentHistoryLines),
      EmploymentGaps = ParseEmploymentGaps(employmentGapLines),
      UniversityDegrees = ParseUniversityDegrees(universityDegreeLines),
      SecondaryEducationHistory = ParseSecondaryEducationHistory(secondaryEducationLines),
      TrainingAndCpd = ParseTrainingAndCpd(trainingLines),
      ProfessionalBodiesMembership = ParseProfessionalBodiesMembership(membershipLines),
      SupportingStatement = ParseSupportingStatement(supportingStatementLines),
      PartTimeOrJobShare = ParsePartTimeOrJobShare(flexibleWorkingText),
      TeacherStatus = ParseTeacherStatus(teacherStatusText),
      AdditionalInformation = ParseAdditionalInformation(additionalInformationText),
    };
  }

  private static List<EmploymentRecord> ParseEmploymentHistory(IReadOnlyList<DocumentLine> lines)
  {
    var records = new List<EmploymentRecord>();

    for (var index = 0; index < lines.Count; index++)
    {
      if (!LineEquals(lines[index], "Employer:"))
      {
        continue;
      }

      var record = new EmploymentRecord
      {
        Employer = AppendWrappedValueContinuation(GetPreviousValue(lines, index), lines, index),
      };

      for (index++; index < lines.Count; index++)
      {
        var text = lines[index].Text;

        if (TextEquals(text, "Employer:"))
        {
          index--;
          break;
        }

        switch (text.ToUpperInvariant())
        {
          case "JOB TITLE:":
            record.JobTitle = GetPreviousValue(lines, index);
            break;
          case "MAIN DUTIES:":
            record.MainDuties = TextCleaning.NormalizeStructuredText(
                CollectFollowingLines(lines, index, "Reason for Leaving:", "Salary:", "Start Date: End Date:", "Employer:"));
            break;
          case "REASON FOR LEAVING:":
            record.ReasonForLeaving = GetPreviousValue(lines, index);
            break;
          case "SALARY:":
            record.SalaryText = NormalizeSalaryText(GetPreviousValue(lines, index));
            break;
          case "START DATE: END DATE:":
            DateRangeParsing.ApplyMonthYearRange(record, GetPreviousValue(lines, index));
            break;
        }
      }

      record.Employer = TrimTrailingValueLine(record.Employer, record.JobTitle);
      record.MainDuties = TrimTrailingStandaloneListMarker(TrimTrailingValueLine(record.MainDuties, record.ReasonForLeaving));
      records.Add(record);
    }

    return records;
  }

  private static EmploymentGapSection ParseEmploymentGaps(IReadOnlyList<DocumentLine> lines)
  {
    var section = new EmploymentGapSection();

    for (var index = 0; index < lines.Count; index++)
    {
      if (LineEquals(lines[index], "Additional reasons for any gaps in your employment history:"))
      {
        section.AdditionalReasons = index + 1 < lines.Count ? TextCleaning.NormalizeInline(lines[index + 1].Text) : null;
        break;
      }

      if (!LineEquals(lines[index], "to"))
      {
        continue;
      }

      var gap = new EmploymentGap();
      var dateLine = index + 1 < lines.Count ? lines[index + 1].Text : string.Empty;
      var descriptionLine = index + 2 < lines.Count ? lines[index + 2].Text : string.Empty;

      var datesMatch = GapDatesPattern().Match(dateLine);
      if (datesMatch.Success)
      {
        gap.StartDate = DateParsing.ParseMonthYear(datesMatch.Groups["start"].Value);
        gap.EndDate = DateParsing.ParseMonthYear(datesMatch.Groups["end"].Value);
      }

      var descriptorMatch = GapDescriptorPattern().Match(descriptionLine);
      if (descriptorMatch.Success)
      {
        gap.DurationText = descriptorMatch.Groups["duration"].Value.Trim();
        gap.BetweenDescription = descriptorMatch.Groups["between"].Value.Trim();
      }

      var reasonLines = new List<DocumentLine>();
      for (var reasonIndex = index + 3; reasonIndex < lines.Count; reasonIndex++)
      {
        var reasonText = lines[reasonIndex].Text;

        if (TextEquals(reasonText, "to")
            || TextEquals(reasonText, "Additional reasons for any gaps in your employment history:"))
        {
          index = reasonIndex - 1;
          break;
        }

        reasonLines.Add(lines[reasonIndex]);
        index = reasonIndex;
      }

      gap.Reason = TextCleaning.NormalizeParagraphText(reasonLines);
      section.Gaps.Add(gap);
    }

    return section;
  }

  private static List<UniversityDegreeRecord> ParseUniversityDegrees(IReadOnlyList<DocumentLine> lines)
  {
    var records = new List<UniversityDegreeRecord>();

    for (var index = 0; index < lines.Count; index++)
    {
      if (!LineEquals(lines[index], "Institution:"))
      {
        continue;
      }

      var record = new UniversityDegreeRecord
      {
        Institution = GetPreviousValue(lines, index),
      };

      for (index++; index < lines.Count; index++)
      {
        var text = lines[index].Text;

        if (TextEquals(text, "Institution:"))
        {
          index--;
          break;
        }

        switch (text.ToUpperInvariant())
        {
          case "COURSE:":
            record.Course = GetPreviousValue(lines, index);
            break;
          case "QUALIFICATION:":
            record.Qualification = GetPreviousValue(lines, index);
            break;
          case "GRADE:":
            record.Grade = GetPreviousValue(lines, index);
            break;
          case "START DATE: END DATE:":
            DateRangeParsing.ApplyMonthYearRange(record, GetPreviousValue(lines, index));
            break;
        }
      }

      records.Add(record);
    }

    return records;
  }

  private static List<SecondaryEducationRecord> ParseSecondaryEducationHistory(IReadOnlyList<DocumentLine> lines)
  {
    var records = new List<SecondaryEducationRecord>();
    var schoolLabelIndices = FindAllLineIndices(lines, "School/College:");

    for (var recordIndex = 0; recordIndex < schoolLabelIndices.Count; recordIndex++)
    {
      var schoolLabelIndex = schoolLabelIndices[recordIndex];
      var nextSchoolLabelIndex = recordIndex + 1 < schoolLabelIndices.Count ? schoolLabelIndices[recordIndex + 1] : lines.Count;
      var blockEndExclusive = recordIndex + 1 < schoolLabelIndices.Count
          ? Math.Max(schoolLabelIndex + 1, nextSchoolLabelIndex - 1)
          : lines.Count;
      var block = lines.Skip(schoolLabelIndex).Take(blockEndExclusive - schoolLabelIndex).ToList();

      var record = new SecondaryEducationRecord
      {
        SchoolCollege = GetPreviousValue(lines, schoolLabelIndex),
      };

      var datesIndex = FindLine(block, "Start Date: End Date:");
      if (datesIndex >= 0)
      {
        DateRangeParsing.ApplyMonthYearRange(record, GetPreviousValue(block, datesIndex));
      }

      var gcseIndex = FindLine(block, "GCSEs or Equivalent:");
      var aLevelIndex = FindLine(block, "GCE 'A' Level or Equivalent:");
      var otherIndex = FindLine(block, "Other:");

      if (gcseIndex >= 0 && aLevelIndex > gcseIndex)
      {
        record.GcsesOrEquivalent = ParseResults([.. block.Skip(gcseIndex + 1).Take(aLevelIndex - gcseIndex - 1)]);
      }

      if (aLevelIndex >= 0 && otherIndex > aLevelIndex)
      {
        record.ALevelsOrEquivalent = ParseResults([.. block.Skip(aLevelIndex + 1).Take(otherIndex - aLevelIndex - 1)]);
      }

      if (otherIndex >= 0)
      {
        record.Other = TextCleaning.NormalizeParagraphText([.. block.Skip(otherIndex + 1)]);
      }

      records.Add(record);
    }

    return records;
  }

  private static List<TrainingRecord> ParseTrainingAndCpd(IReadOnlyList<DocumentLine> lines)
  {
    var records = new List<TrainingRecord>();

    for (var index = 0; index < lines.Count; index++)
    {
      if (!LineEquals(lines[index], "Training Course/Title:"))
      {
        continue;
      }

      var record = new TrainingRecord
      {
        Title = GetPreviousValue(lines, index),
      };

      for (index++; index < lines.Count; index++)
      {
        var text = lines[index].Text;

        if (TextEquals(text, "Training Course/Title:"))
        {
          index--;
          break;
        }

        switch (text.ToUpperInvariant())
        {
          case "ORGANISING BODY:":
            record.OrganisingBody = GetPreviousValue(lines, index);
            break;
          case "QUALIFICATION:":
            record.Qualification = GetPreviousValue(lines, index);
            break;
          case "DATE:":
            record.Date = DateParsing.ParseMonthYear(GetPreviousValue(lines, index));
            break;
        }
      }

      records.Add(record);
    }

    return records;
  }

  private static List<ProfessionalBodyMembershipRecord> ParseProfessionalBodiesMembership(IReadOnlyList<DocumentLine> lines)
  {
    var fieldLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Professional Body:",
            "Membership Level:",
            "Date:",
        };
    var orderedLabels = new[]
    {
            "Professional Body:",
            "Membership Level:",
            "Date:",
        };

    var values = lines
        .Select(line => TextCleaning.NormalizeInline(line.Text))
        .Where(text => !string.IsNullOrWhiteSpace(text) && !NumberedSectionHeadingPattern().IsMatch(text))
        .ToList();

    if (values.Count == 0)
    {
      return [];
    }

    var records = new List<ProfessionalBodyMembershipRecord>();
    var cursor = 0;

    while (true)
    {
      var professionalBodyLabelIndex = FindToken(values, orderedLabels[0], cursor);
      if (professionalBodyLabelIndex < 0)
      {
        break;
      }

      var membershipLevelLabelIndex = FindToken(values, orderedLabels[1], professionalBodyLabelIndex + 1);
      var dateLabelIndex = membershipLevelLabelIndex < 0
          ? -1
          : FindToken(values, orderedLabels[2], membershipLevelLabelIndex + 1);

      if (membershipLevelLabelIndex < 0 || dateLabelIndex < 0)
      {
        break;
      }

      var recordUsesTrailingLabels = UsesTrailingFieldLabels(values, cursor, professionalBodyLabelIndex, membershipLevelLabelIndex);
      var record = recordUsesTrailingLabels
          ? ParseTrailingLabelProfessionalBody(values, cursor, professionalBodyLabelIndex, membershipLevelLabelIndex, dateLabelIndex, fieldLabels)
          : ParseLeadingLabelProfessionalBody(values, professionalBodyLabelIndex, membershipLevelLabelIndex, dateLabelIndex, fieldLabels);

      if (HasProfessionalBodyMembership(record))
      {
        records.Add(record);
      }

      cursor = recordUsesTrailingLabels
          ? dateLabelIndex + 1
          : FindToken(values, orderedLabels[0], dateLabelIndex + 1) switch
          {
            >= 0 and var nextProfessionalBodyLabelIndex => nextProfessionalBodyLabelIndex,
            _ => values.Count,
          };
    }

    return records;
  }

  private static bool UsesTrailingFieldLabels(IReadOnlyList<string> values, int recordStartIndex, int professionalBodyLabelIndex, int membershipLevelLabelIndex)
  {
    var hasValueBeforeProfessionalBody = HasFieldValue(values, recordStartIndex, professionalBodyLabelIndex);
    var hasValueAfterProfessionalBody = HasFieldValue(values, professionalBodyLabelIndex + 1, membershipLevelLabelIndex);

    if (hasValueBeforeProfessionalBody != hasValueAfterProfessionalBody)
    {
      return hasValueBeforeProfessionalBody;
    }

    return hasValueBeforeProfessionalBody;
  }

  private static ProfessionalBodyMembershipRecord ParseTrailingLabelProfessionalBody(
      IReadOnlyList<string> values,
      int recordStartIndex,
      int professionalBodyLabelIndex,
      int membershipLevelLabelIndex,
      int dateLabelIndex,
      ISet<string> fieldLabels)
  {
    return new ProfessionalBodyMembershipRecord
    {
      ProfessionalBody = ReadFieldValue(values, recordStartIndex, professionalBodyLabelIndex, fieldLabels),
      MembershipLevel = ReadFieldValue(values, professionalBodyLabelIndex + 1, membershipLevelLabelIndex, fieldLabels),
      Date = ParseMembershipDate(ReadFieldValue(values, membershipLevelLabelIndex + 1, dateLabelIndex, fieldLabels)),
    };
  }

  private static ProfessionalBodyMembershipRecord ParseLeadingLabelProfessionalBody(
      List<string> values,
      int professionalBodyLabelIndex,
      int membershipLevelLabelIndex,
      int dateLabelIndex,
      ISet<string> fieldLabels)
  {
    var nextProfessionalBodyLabelIndex = FindToken(values, "Professional Body:", dateLabelIndex + 1);
    var recordEndIndex = nextProfessionalBodyLabelIndex >= 0 ? nextProfessionalBodyLabelIndex : values.Count;

    return new ProfessionalBodyMembershipRecord
    {
      ProfessionalBody = ReadFieldValue(values, professionalBodyLabelIndex + 1, membershipLevelLabelIndex, fieldLabels),
      MembershipLevel = ReadFieldValue(values, membershipLevelLabelIndex + 1, dateLabelIndex, fieldLabels),
      Date = ParseMembershipDate(ReadFieldValue(values, dateLabelIndex + 1, recordEndIndex, fieldLabels)),
    };
  }

  private static bool HasFieldValue(IReadOnlyList<string> values, int startIndex, int endExclusive)
  {
    for (var index = startIndex; index < endExclusive; index++)
    {
      if (!string.IsNullOrWhiteSpace(values[index]))
      {
        return true;
      }
    }

    return false;
  }

  private static string? ReadFieldValue(IReadOnlyList<string> values, int startIndex, int endExclusive, ISet<string> fieldLabels)
  {
    if (endExclusive <= startIndex)
    {
      return null;
    }

    var fieldValueLines = values
        .Skip(startIndex)
        .Take(endExclusive - startIndex)
        .Where(text => !fieldLabels.Contains(text))
        .ToList();

    return fieldValueLines.Count == 0
        ? null
        : TextCleaning.NormalizeInline(string.Join(' ', fieldValueLines));
  }

  private static string? ParseMembershipDate(string? value)
  {
    return DateParsing.ParseFlexibleDate(value) ?? value;
  }

  private static string? NormalizeSalaryText(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return value;
    }

    return SalaryPoundSignPattern().Replace(value, "\u00A3");
  }

  private static int FindToken(List<string> values, string token, int startIndex)
  {
    for (var index = startIndex; index < values.Count; index++)
    {
      if (TextEquals(values[index], token))
      {
        return index;
      }
    }

    return -1;
  }

  private static bool HasProfessionalBodyMembership(ProfessionalBodyMembershipRecord record)
  {
    return !string.IsNullOrWhiteSpace(record.ProfessionalBody)
        || !string.IsNullOrWhiteSpace(record.MembershipLevel)
        || !string.IsNullOrWhiteSpace(record.Date);
  }

  private static string? ParseSupportingStatement(IReadOnlyList<DocumentLine> lines)
  {
    return TextCleaning.NormalizeParagraphText(lines);
  }

  private static bool? ParsePartTimeOrJobShare(string sectionText)
  {
    var fields = OrderedFieldParser.Parse(sectionText,
        "Are you applying to do this job on a part-time/job share basis?:");

    return ValueParsing.ParseYesNo(fields.GetValueOrDefault("Are you applying to do this job on a part-time/job share basis?:"));
  }

  private static ApplicantRecord ParseApplicant(string sectionText)
  {
    var fields = OrderedFieldParser.Parse(sectionText,
        "Title:",
        "First Name(s):",
        "Middle Name(s):",
        "Last Name(s):",
        "Former Name(s):",
        "Preferred Name(s):",
        "Current Address:",
        "Daytime Contact Number:",
        "Mobile Number:",
        "Email Address:",
        "Do you require sponsorship to work in the UK?:",
        "National Insurance Number:");

    return new ApplicantRecord
    {
      Title = fields.GetValueOrDefault("Title:"),
      FirstName = fields.GetValueOrDefault("First Name(s):"),
      LastName = fields.GetValueOrDefault("Last Name(s):"),
      PreferredName = EmptyToNull(fields.GetValueOrDefault("Preferred Name(s):")),
      RequiresUkSponsorship = ValueParsing.ParseYesNo(fields.GetValueOrDefault("Do you require sponsorship to work in the UK?:")),
    };
  }

  private static TeacherStatusRecord ParseTeacherStatus(string sectionText)
  {
    var fields = OrderedFieldParser.Parse(sectionText,
        "Qualified Teacher Status:",
        "Teacher Reference Number:",
        "Date Attained:",
        "Expected Date of Completion:",
        "If you gained QTS after 7th May 1999, have you completed the statutory NQT / ECT induction period?:",
        "If you have an overseas teaching qualification, has this been recognised as meeting the same standards as qualified teacher status in England?:",
        "Are you subject to a teacher prohibition order, or an interim prohibition order, issued by the secretary of state, as a result of misconduct?:",
        "Are you subject to a General Teaching Council sanction or restriction?:");

    return new TeacherStatusRecord
    {
      QualifiedTeacherStatus = fields.GetValueOrDefault("Qualified Teacher Status:"),
      TeacherReferenceNumber = fields.GetValueOrDefault("Teacher Reference Number:"),
      DateAttained = DateParsing.ParseFlexibleDate(fields.GetValueOrDefault("Date Attained:")),
      ExpectedDateOfCompletion = DateParsing.ParseFlexibleDate(fields.GetValueOrDefault("Expected Date of Completion:")),
      CompletedStatutoryInduction = ValueParsing.ParseYesNo(fields.GetValueOrDefault("If you gained QTS after 7th May 1999, have you completed the statutory NQT / ECT induction period?:")),
      OverseasQualificationRecognised = EmptyToNull(fields.GetValueOrDefault("If you have an overseas teaching qualification, has this been recognised as meeting the same standards as qualified teacher status in England?:")),
      SubjectToTeacherProhibitionOrder = ValueParsing.ParseYesNo(fields.GetValueOrDefault("Are you subject to a teacher prohibition order, or an interim prohibition order, issued by the secretary of state, as a result of misconduct?:")),
      SubjectToTeachingCouncilSanction = ValueParsing.ParseYesNo(fields.GetValueOrDefault("Are you subject to a General Teaching Council sanction or restriction?:")),
    };
  }

  private static AdditionalInformationRecord ParseAdditionalInformation(string sectionText)
  {
    const string relatedToOrganisationKey = "RelatedToOrganisation";
    const string previouslyEmployedByOrganisationKey = "PreviouslyEmployedByOrganisation";

    var fields = OrderedFieldParser.ParseRegex(sectionText,
        (relatedToOrganisationKey, RelatedToOrganisationLabelPattern()),
        (previouslyEmployedByOrganisationKey, PreviouslyEmployedByOrganisationLabelPattern()),
        ("Do you hold any other appointment that would continue if you were appointed to this job?:", HoldsOtherAppointmentLabelPattern()),
        ("Are you required to provide notice for your current employment?:", RequiresNoticeLabelPattern()));

    return new AdditionalInformationRecord
    {
      RelatedToOrganisation = ValueParsing.ParseYesNo(fields.GetValueOrDefault(relatedToOrganisationKey)),
      PreviouslyEmployedByOrganisation = ValueParsing.ParseYesNo(fields.GetValueOrDefault(previouslyEmployedByOrganisationKey)),
      HoldsOtherAppointment = ValueParsing.ParseYesNo(fields.GetValueOrDefault("Do you hold any other appointment that would continue if you were appointed to this job?:")),
      RequiresNotice = ValueParsing.ParseYesNo(fields.GetValueOrDefault("Are you required to provide notice for your current employment?:")),
    };
  }

  private static List<AcademicResultRecord> ParseResults(IReadOnlyList<DocumentLine> lines)
  {
    var results = new List<AcademicResultRecord>();

    foreach (var line in lines)
    {
      var match = ResultLinePattern().Match(line.Text);
      if (!match.Success)
      {
        continue;
      }

      results.Add(new AcademicResultRecord
      {
        Subject = match.Groups["subject"].Value.Trim(),
        Grade = match.Groups["grade"].Value.Trim(),
      });
    }

    return results;
  }

  [GeneratedRegex(@"^\((?<duration>[^)]+?)\s+between\s+(?<between>.+)\)$", RegexOptions.IgnoreCase)]
  private static partial Regex GapDescriptorPattern();

  [GeneratedRegex(@"^(?<start>(?:" + DateParsing.MonthPattern + @")\s+\d{4})\s+(?<end>(?:" + DateParsing.MonthPattern + @")\s+\d{4})$", RegexOptions.IgnoreCase)]
  private static partial Regex GapDatesPattern();

  [GeneratedRegex(@"^(?<subject>.+?)\s+(?<grade>[A-Za-z0-9:./*+-]+)$")]
  private static partial Regex ResultLinePattern();

  [GeneratedRegex(@"^#(?=\d)")]
  private static partial Regex SalaryPoundSignPattern();

  [GeneratedRegex(@"Areyou,toyourknowledge,relatedtoorhaveapersonalrelationshipwithanygovernor,academytrustee,member,localgovernor,pupiloremployeeat.+?oranymemberschoolswithintheacademytrust\?:", RegexOptions.IgnoreCase)]
  private static partial Regex RelatedToOrganisationLabelPattern();

  [GeneratedRegex(@"Areyou,orhaveyoupreviouslybeen,employed/engagedby.+?oranymemberschoolswhetherasanemployee,agencyworkerorotherwisewithintheacademytrust\?:", RegexOptions.IgnoreCase)]
  private static partial Regex PreviouslyEmployedByOrganisationLabelPattern();

  [GeneratedRegex(@"Doyouholdanyotherappointmentthatwouldcontinueifyouwereappointedtothisjob\?:", RegexOptions.IgnoreCase)]
  private static partial Regex HoldsOtherAppointmentLabelPattern();

  [GeneratedRegex(@"Areyourequiredtoprovidenoticeforyourcurrentemployment\?:", RegexOptions.IgnoreCase)]
  private static partial Regex RequiresNoticeLabelPattern();
}
