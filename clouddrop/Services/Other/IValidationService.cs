namespace clouddrop.Services.Other;

public interface IValidationService
{
    public bool ValidateSignUpRequest(SignUpRequest model, out string error);
}