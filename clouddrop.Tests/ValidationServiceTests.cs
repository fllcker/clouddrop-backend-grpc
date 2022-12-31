using clouddrop;
using clouddrop.Services.Other;

namespace clouddrop.Tests;

public class ValidationServiceTests
{
    [Fact]
    public void EmailTest()
    {
        ValidationService validationService = new ValidationService();

        var result1 = validationService.EmailVerify("fllcker@gmail.com");
        var result2 = validationService.EmailVerify("fllckergmail.com");
        var result3 = validationService.EmailVerify("fllcker@gmailcom");
        var result4 = validationService.EmailVerify("fllcker@neemail.com");
        
        Assert.True(result1);
        Assert.False(result2);
        Assert.False(result3);
        Assert.True(result4);
    }
}