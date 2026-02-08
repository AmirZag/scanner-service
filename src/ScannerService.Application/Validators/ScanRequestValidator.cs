using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentValidation;
using ScannerService.Application.DTOs;

namespace ScannerService.Application.Validators;

public class ScanRequestValidator : AbstractValidator<ScanRequestDto>
{
    public ScanRequestValidator()
    {
        RuleFor(x => x.ProfileId)
            .GreaterThan(0)
            .WithMessage("ProfileId must be greater than 0");
    }
}
