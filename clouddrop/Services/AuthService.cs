using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AutoMapper;
using clouddrop.Data;
using clouddrop.Models;
using clouddrop.Services.Other;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace clouddrop.Services;

public class AuthService : clouddrop.AuthService.AuthServiceBase
{
    private readonly DBC _dbc;
    private readonly IValidationService _validationService;
    private readonly IMapper _mapper;
    private readonly IConfiguration _configuration;

    public AuthService(DBC dbc, IMapper mapper, IConfiguration configuration, IValidationService validationService)
    {
        _dbc = dbc;
        _validationService = validationService;
        _mapper = mapper;
        _configuration = configuration;
    }
    
    public string CreateToken(User user)
    {
        List<Claim> claims = new List<Claim>
        {
            new Claim("Id", user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email)
        };
        var key = new SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes(_configuration.GetSection("Config:Secret").Value!));

        var cred = new SigningCredentials(key, SecurityAlgorithms.HmacSha512Signature);

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.Now.AddDays(7),
            signingCredentials: cred);

        var jwt = new JwtSecurityTokenHandler().WriteToken(token);
        return jwt;
    }

    public override async Task<TokenResponse> SignUp(SignUpRequest request, ServerCallContext context)
    {
        if (await _dbc.Users.CountAsync(v => v.Email == request.Email) != 0)
            throw new RpcException(new Status(StatusCode.AlreadyExists, "Email already exists!"));
        
        if (!_validationService.ValidateSignUpRequest(request, out string modelError))
            throw new RpcException(new Status(StatusCode.Unknown, modelError));
        
        var user = _mapper.Map<User>(request);
        user.Password = BCrypt.Net.BCrypt.HashPassword(user.Password); // hashing password
        
        // creating storage for user // TODO: Later create folder for user
        user.Storage = new Storage() { User = user };
        var homeDir = new Content()
            { ContentType = ContentType.Folder, Name = "home", Path = "home", Storage = user.Storage };

        _dbc.Users.Add(user);
        _dbc.Storages.Add(user.Storage);
        _dbc.Contents.Add(homeDir);
        await _dbc.SaveChangesAsync();
        
        // creating token
        var token = CreateToken(user);
        return await Task.FromResult(new TokenResponse() { Token = token });
    }

    public override async Task<TokenResponse> SignIn(SignInRequest request, ServerCallContext context)
    {
        var candidate = await _dbc.Users.SingleOrDefaultAsync(v => v.Email == request.Email);
        if (candidate == null)
            throw new RpcException(new Status(StatusCode.NotFound, "User with this email not found!"));
        if (!BCrypt.Net.BCrypt.Verify(request.Password, candidate.Password))
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Wrong data!"));

        var token = CreateToken(candidate);
        return await Task.FromResult(new TokenResponse() { Token = token });
    }
}