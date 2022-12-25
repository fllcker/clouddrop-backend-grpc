namespace clouddrop.Services.Other;

public class ValidationService : IValidationService
{
    public bool ValidateSignUpRequest(SignUpRequest model, out string error)
    {
        error = "";
        if (!model.Email.Contains("@") ||
            !model.Email.Contains("."))
            error = "This is not email!";
            

        if (model.Password.Length < 6)
            error = "Password must be > 6 symbols!";

        return error == "";
    }
}