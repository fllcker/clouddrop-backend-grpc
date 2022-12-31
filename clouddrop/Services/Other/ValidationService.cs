using System.Text.RegularExpressions;

namespace clouddrop.Services.Other;

public class ValidationService : IValidationService
{
    public bool ValidateSignUpRequest(SignUpRequest model, out string error)
    {
        error = "";
        if (!EmailVerify(model.Email))
            error = "This is not email!";

        if (model.Password.Length < 6)
            error = "Password must be > 6 symbols!";

        return error == "";
    }

    public bool EmailVerify(string email)
    {
        Regex regex = new Regex(@"^([\w\.\-]+)@([\w\-]+)((\.(\w){2,3})+)$");
        Match match = regex.Match(email);
        return match.Success;
    }
}