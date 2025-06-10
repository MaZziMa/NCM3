using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NCM3.Models;
using SnmpSharpNet;

namespace NCM3.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SNMPTestController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<SNMPTestController> _logger;

        public SNMPTestController(IConfiguration configuration, ILogger<SNMPTestController> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        [HttpPost]
        public IActionResult TestSNMPCredentials([FromBody] SNMPTestRequest request)
        {
            if (string.IsNullOrEmpty(request.IpAddress))
            {
                return BadRequest(new { success = false, message = "IP Address is required" });
            }            if (string.IsNullOrEmpty(request.Community))
            {
                // Use default community if none provided
                request.Community = _configuration.GetValue<string>("ChangeDetection:Strategies:SNMPPolling:Community", "public");
            }

            if (string.IsNullOrEmpty(request.Version))
            {
                // Use default version if none provided
                request.Version = _configuration.GetValue<string>("ChangeDetection:Strategies:SNMPPolling:Version", "Auto");
            }            try
            {                // Validate IP address format
                if (!IPAddress.TryParse(request.IpAddress, out IPAddress? ipAddressResult) || ipAddressResult == null)
                {
                    return BadRequest(new { 
                        success = false, 
                        message = $"Invalid IP address format: {request.IpAddress}" 
                    });
                }
                
                IPAddress ipAddress = ipAddressResult;
                
                var community = new OctetString(request.Community);
                var param = new AgentParameters(community);
                
                // Get SNMP settings from config
                int port = _configuration.GetValue<int>("ChangeDetection:Strategies:SNMPPolling:Port", 161);
                int timeout = _configuration.GetValue<int>("ChangeDetection:Strategies:SNMPPolling:Timeout", 2000);
                int retries = _configuration.GetValue<int>("ChangeDetection:Strategies:SNMPPolling:Retries", 0);
                
                var target = new UdpTarget(ipAddress, port, timeout, retries);
                
                try
                {
                    // Create Pdu for SNMP GET
                    var pdu = new Pdu(PduType.Get);                    // Use system description OID (common on all SNMP devices)
                    var oid = new Oid("1.3.6.1.2.1.1.1.0"); // sysDescr.0
                    pdu.VbList.Add(oid);
                      // Determine SNMP version to use
                    SnmpPacket? result = null;
                    string snmpVersion = request.Version?.ToLower() ?? "auto";
                    string versionUsed = "";
                    
                    if (snmpVersion == "v1" || snmpVersion == "1")
                    {
                        // Use SNMPv1
                        param.Version = SnmpVersion.Ver1;
                        result = target.Request(pdu, param);
                        versionUsed = "SNMPv1";
                    }
                    else if (snmpVersion == "v2" || snmpVersion == "v2c" || snmpVersion == "2")
                    {
                        // Use SNMPv2c
                        param.Version = SnmpVersion.Ver2;
                        result = target.Request(pdu, param);
                        versionUsed = "SNMPv2c";
                    }
                    else // "auto" or any other value
                    {
                        // Try SNMPv2c first, then fallback to SNMPv1 if it fails
                        try
                        {
                            param.Version = SnmpVersion.Ver2;
                            result = target.Request(pdu, param);
                            versionUsed = "SNMPv2c";
                        }
                        catch (Exception ex)
                        {
                            _logger.LogInformation("SNMPv2c request failed, falling back to SNMPv1: {ErrorMessage}", ex.Message);
                            
                            // Reset PDU for new request
                            pdu = new Pdu(PduType.Get);
                            pdu.VbList.Add(oid);
                            
                            param.Version = SnmpVersion.Ver1;
                            result = target.Request(pdu, param);
                            versionUsed = "SNMPv1";
                        }
                    }
                    
                    // Process the result based on the actual type returned
                    if (result != null)
                    {
                        if (result is SnmpV2Packet v2Packet && v2Packet.Pdu.ErrorStatus == 0)
                        {
                            var sysdescr = v2Packet.Pdu.VbList[0].Value.ToString();
                            return Ok(new { 
                                success = true, 
                                message = $"SNMP test successful using {versionUsed}", 
                                sysDescr = sysdescr,
                                version = versionUsed
                            });
                        }
                        else if (result is SnmpV1Packet v1Packet && v1Packet.Pdu.ErrorStatus == 0)
                        {
                            var sysdescr = v1Packet.Pdu.VbList[0].Value.ToString();
                            return Ok(new { 
                                success = true, 
                                message = $"SNMP test successful using {versionUsed}", 
                                sysDescr = sysdescr,
                                version = versionUsed
                            });
                        }
                        else
                        {
                            // Handle case where we got a response but with an error
                            string errorStatus = "unknown";
                            int errorIndex = -1;
                            
                            if (result is SnmpV2Packet v2P)
                            {
                                errorStatus = v2P.Pdu.ErrorStatus.ToString();
                                errorIndex = v2P.Pdu.ErrorIndex;
                            }
                            else if (result is SnmpV1Packet v1P)
                            {
                                errorStatus = v1P.Pdu.ErrorStatus.ToString();
                                errorIndex = v1P.Pdu.ErrorIndex;
                            }
                            
                            return BadRequest(new { 
                                success = false, 
                                message = $"SNMP GET failed using {versionUsed}. Error: {errorStatus}, Index: {errorIndex}", 
                                version = versionUsed
                            });
                        }
                    }
                    else
                    {
                        return BadRequest(new { 
                            success = false, 
                            message = $"No response received from SNMP request using {versionUsed}"
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error testing SNMP for IP {IPAddress}: {ErrorMessage}",
                        request.IpAddress, ex.Message);
                    
                    return BadRequest(new { 
                        success = false, 
                        message = $"Error: {ex.Message}" 
                    });
                }
                finally
                {
                    target.Close();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SNMP test: {ErrorMessage}", ex.Message);
                return BadRequest(new { 
                    success = false, 
                    message = $"Error: {ex.Message}" 
                });
            }
        }
    }    public class SNMPTestRequest
    {
        public string? IpAddress { get; set; }
        public string? Community { get; set; }
        public string? Version { get; set; }
    }
}
