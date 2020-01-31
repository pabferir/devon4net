﻿using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Devon4Net.Infrastructure.Common;
using Devon4Net.Infrastructure.Common.Options.JWT;
using Microsoft.IdentityModel.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Devon4Net.Infrastructure.JWT.Handlers
{
    public class JwtHandler : IJwtHandler
    {
        public JwtHandler(JwtOptions jwtOptions)
        {
            JwtOptions = jwtOptions;
            if (JwtOptions != null)
            {
                SetupJwtSecurity();
            }
            else
            {
                throw new ArgumentNullException("Cannot create the JWT Handler. JWTOptions are null.");
            }
        }

        private SecurityKey IssuerSigningKey { get; set; }
        private EncryptingCredentials EncryptingCredentials { get; set; }
        private SigningCredentials SigningCredentials { get; set; }
        private X509Certificate2 Certificate { get; set; }
        private static SecurityKey SecurityKey { get; set; }
        private JwtOptions JwtOptions { get; set; }

        private void SetupJwtSecurity()
        {
            if (JwtOptions?.Security == null) return;

            if (!string.IsNullOrEmpty(JwtOptions.Security.SecretKey))
            {
                GetSigningCredentialsFromKey(JwtOptions.Security.SecretKey);
            }
            else
            if (!string.IsNullOrEmpty(JwtOptions.Security.Certificate) &&
                !string.IsNullOrEmpty(JwtOptions.Security.CertificatePassword))
            {
                GetSigningCredentialsFromCertificate(JwtOptions.Security.Certificate,
                    JwtOptions.Security.CertificatePassword);
            }
        }

        public string CreateClientToken(List<Claim> clientClaims)
        {
            IdentityModelEventSource.ShowPII = true;
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Issuer = JwtOptions.Issuer,
                Audience = JwtOptions.Audience,
                Subject = new ClaimsIdentity(clientClaims),
                Expires = DateTime.UtcNow.AddMinutes(Convert.ToInt16(JwtOptions.ClockSkew)),
                IssuedAt = DateTime.Now,
                Claims = clientClaims.ToDictionary(x => x.Type, x => x.Value as object),
                SigningCredentials = SigningCredentials
            };
            return new JwtSecurityTokenHandler().CreateEncodedJwt(tokenDescriptor);
        }

        public IEnumerable<Claim> GetUserClaims(string jwtToken)
        {
            var handler = new JwtSecurityTokenHandler();

            var claimsPrincipal = handler.ValidateToken(jwtToken,
                new TokenValidationParameters
                {
                    ValidAudience = JwtOptions.Audience,
                    ValidIssuer = JwtOptions.Issuer,
                    RequireSignedTokens = false,
                    TokenDecryptionKey = SecurityKey,
                    IssuerSigningKey = SecurityKey
                }, out _);

            return claimsPrincipal.Claims;
        }

        public string GetClaimValue(IEnumerable<Claim> claimList, string claim)
        {
            if (claimList == null || !claimList.Any()) return string.Empty;
            return claimList.FirstOrDefault(x => x.Type == claim)?.Value;
        }

        public string GetClaimValue(string token, string claim)
        {
            if (string.IsNullOrEmpty(token)) return string.Empty;
            var claimList = GetUserClaims(token);
            return GetClaimValue(claimList, claim);
        }

        public SecurityKey GetIssuerSigningKey()
        {
            return IssuerSigningKey;
        }

        private void GetSigningCredentialsFromKey(string secretKey)
        {
            var lengthAlgorithm = JwtOptions.Security.SecretKeyLengthAlgorithm ?? SecurityAlgorithms.Aes256KW;
            var secretKeyEncryptionAlgorithm = JwtOptions.Security.SecretKeyEncryptionAlgorithm ?? SecurityAlgorithms.Aes128CbcHmacSha256;
            var key = new SymmetricSecurityKey(Encoding.Default.GetBytes(secretKey));
            SecurityKey = key;
            IssuerSigningKey = new SymmetricSecurityKey(key.Key);
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha512Signature);
            EncryptingCredentials = new EncryptingCredentials(IssuerSigningKey, lengthAlgorithm, secretKeyEncryptionAlgorithm);
        }

        private void GetSigningCredentialsFromCertificate(string certificate, string password)
        {
            try
            {
                var certificateEncryptionAlgorithm = JwtOptions.Security.CertificateEncryptionAlgorithm ?? SecurityAlgorithms.RsaSha512;
                Certificate = new X509Certificate2(File.ReadAllBytes(FileOperations.GetFileFullPath(certificate)), password,  X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
                SecurityKey = new X509SecurityKey(Certificate);
                IssuerSigningKey = new X509SecurityKey(Certificate);
                EncryptingCredentials = new X509EncryptingCredentials(Certificate);
                SigningCredentials = new SigningCredentials(IssuerSigningKey, certificateEncryptionAlgorithm);
            }
            catch (CryptographicException ex)
            {
                Console.WriteLine(ex);
                throw;
            }
            catch (ArgumentNullException ex)
            {
                Console.WriteLine(ex);
                throw;
            }
        }
    }
}