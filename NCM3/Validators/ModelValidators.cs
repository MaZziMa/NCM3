using System;
using FluentValidation;
using NCM3.Models;

namespace NCM3.Validators
{
    public class RouterValidator : AbstractValidator<Router>
    {
        public RouterValidator()
        {
            RuleFor(x => x.Hostname)
                .NotEmpty().WithMessage("Tên Hostname không được để trống")
                .MaximumLength(100).WithMessage("Tên Hostname không được vượt quá 100 ký tự")
                .Matches("^[a-zA-Z0-9.-]+$").WithMessage("Tên Hostname chỉ được chứa chữ cái, số, dấu chấm và dấu gạch ngang");
                
            RuleFor(x => x.IpAddress)
                .NotEmpty().WithMessage("Địa chỉ IP không được để trống")
                .Matches(@"^(([0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])\.){3}([0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])$")
                .WithMessage("Địa chỉ IP không hợp lệ");
                
            RuleFor(x => x.Username)
                .NotEmpty().WithMessage("Tên đăng nhập không được để trống")
                .MaximumLength(50).WithMessage("Tên đăng nhập không được vượt quá 50 ký tự");
                
            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Mật khẩu không được để trống");
        }
    }
    
    public class RouterConfigurationValidator : AbstractValidator<RouterConfiguration>
    {
        public RouterConfigurationValidator()
        {
            RuleFor(x => x.RouterId)
                .GreaterThan(0).WithMessage("RouterId phải lớn hơn 0");
                
            RuleFor(x => x.BackupDate)
                .NotEmpty().WithMessage("Ngày sao lưu không được để trống");
                
            RuleFor(x => x.Content)
                .NotEmpty().WithMessage("Nội dung cấu hình không được để trống");
        }
    }
    
    public class ComplianceRuleValidator : AbstractValidator<ComplianceRule>
    {
        public ComplianceRuleValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Tên quy tắc không được để trống")
                .MaximumLength(100).WithMessage("Tên quy tắc không được vượt quá 100 ký tự");
                
            RuleFor(x => x.Pattern)
                .NotEmpty().WithMessage("Biểu thức tìm kiếm không được để trống");
        }
    }
    
    public class ConfigTemplateValidator : AbstractValidator<ConfigTemplate>
    {
        public ConfigTemplateValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Tên template không được để trống")
                .MaximumLength(100).WithMessage("Tên template không được vượt quá 100 ký tự");
                
            RuleFor(x => x.Content)
                .NotEmpty().WithMessage("Nội dung template không được để trống");
        }
    }
}
