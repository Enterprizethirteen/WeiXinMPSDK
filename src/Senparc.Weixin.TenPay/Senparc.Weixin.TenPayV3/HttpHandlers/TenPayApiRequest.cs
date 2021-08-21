﻿#region Apache License Version 2.0
/*----------------------------------------------------------------

Copyright 2021 Jeffrey Su & Suzhou Senparc Network Technology Co.,Ltd.

Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file
except in compliance with the License. You may obtain a copy of the License at

http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software distributed under the
License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND,
either express or implied. See the License for the specific language governing permissions
and limitations under the License.

Detail: https://github.com/JeffreySu/WeiXinMPSDK/blob/master/license.md

----------------------------------------------------------------*/
#endregion Apache License Version 2.0

/*----------------------------------------------------------------
    Copyright (C) 2021 Senparc
  
    文件名：TenPayApiRequest.cs
    文件功能描述：微信支付V3接口请求
    
    
    创建标识：Senparc - 20210815
    
----------------------------------------------------------------*/

using Senparc.CO2NET.Extensions;
using Senparc.CO2NET.Helpers;
using Senparc.Weixin.Entities;
using Senparc.Weixin.TenPayV3.Apis.BasePay.Entities;
using Senparc.Weixin.TenPayV3.Helpers;
using Senparc.Weixin.TenPayV3.HttpHandlers;
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Senparc.Weixin.TenPayV3
{
    /// <summary>
    /// 微信支付 API 请求
    /// </summary>
    public class TenPayApiRequest
    {
        private ISenparcWeixinSettingForTenpayV3 _tenpayV3Setting;

        public TenPayApiRequest(ISenparcWeixinSettingForTenpayV3 senparcWeixinSettingForTenpayV3 = null)
        {
            _tenpayV3Setting = senparcWeixinSettingForTenpayV3 ?? Senparc.Weixin.Config.SenparcWeixinSetting.TenpayV3Setting;
        }

        /// <summary>
        /// 设置 HTTP 请求头
        /// </summary>
        /// <param name="client"></param>
        public void SetHeader(HttpClient client)
        {
            //ACCEPT header
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

            //User-Agent header
            var userAgentValues = UserAgentValues.Instance;
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Senparc.Weixin.TenPayV3-C#", userAgentValues.TenPayV3Version));
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue($"(Senparc.Weixin {userAgentValues.SenparcWeixinVersion})"));
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(".NET", userAgentValues.RuntimeVersion));
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue($"({userAgentValues.OSVersion})"));
        }

        /// <summary>
        /// 请求参数，获取结果
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="url"></param>
        /// <param name="data">如果为 GET 请求，此参数可为 null</param>
        /// <returns></returns>
        public async Task<T> RequestAsync<T>(string url, object data, int timeOut = Config.TIME_OUT, ApiRequestMethod requestMethod = ApiRequestMethod.POST) where T : ReturnJsonBase, new()
        {
            //var co2netHttpClient = CO2NET.HttpUtility.RequestUtility.HttpPost_Common_NetCore(serviceProvider, url, out var hc, contentType: "application/json");

            //设置参数
            var mchid = _tenpayV3Setting.TenPayV3_MchId;
            var ser_no = _tenpayV3Setting.TenPayV3_SerialNumber;
            var privateKey = _tenpayV3Setting.TenPayV3_PrivateKey;

            //使用微信支付参数，配置 HttpHandler
            TenPayHttpHandler httpHandler = new(mchid, ser_no, privateKey);

            //创建 HttpClient
            HttpClient client = new HttpClient(httpHandler);
            //设置超时时间
            client.Timeout = TimeSpan.FromMilliseconds(timeOut);

            //设置 HTTP 请求头
            SetHeader(client);

            HttpResponseMessage responseMessage = null;
            switch (requestMethod)
            {
                case ApiRequestMethod.GET:
                    responseMessage = await client.GetAsync(url);
                    WeixinTrace.Log(url); //记录Get的Json数据
                    break;
                case ApiRequestMethod.POST:
                case ApiRequestMethod.PUT:
                case ApiRequestMethod.PATCH:
                    //检查是否为空
                    _ = data ?? throw new ArgumentNullException($"{nameof(data)} 不能为 null！");

                    //设置请求 Json 字符串
                    //var jsonString = SerializerHelper.GetJsonString(data, new CO2NET.Helpers.Serializers.JsonSetting(true));
                    string jsonString = data.ToJson(false, new Newtonsoft.Json.JsonSerializerSettings() { NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore });
                    WeixinTrace.SendApiPostDataLog(url, jsonString); //记录Post的Json数据

                    //设置 HttpContent
                    var hc = new StringContent(jsonString, Encoding.UTF8, mediaType: "application/json");
                    //获取响应结果
                    responseMessage = requestMethod switch
                    {
                        ApiRequestMethod.POST => await client.PostAsync(url, hc),
                        ApiRequestMethod.PUT => await client.PutAsync(url, hc),
                        ApiRequestMethod.PATCH => await client.PatchAsync(url, hc),
                        _ => throw new ArgumentOutOfRangeException(nameof(requestMethod))
                    };
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(requestMethod));
            }

            //检查响应代码
            TenPayApiResultCode resutlCode = TenPayApiResultCode.TryGetCode(responseMessage.StatusCode);

            //TODO:待测试 加入验证签名
            //获取响应结果
            T result = null;
            if (resutlCode.Success)
            {
                string content = await responseMessage.Content.ReadAsStringAsync();

                //TODO:待测试
                //验证微信签名
                //result.Signed = VerifyTenpaySign(responseMessage.Headers, content);
                var wechatpayTimestamp = responseMessage.Headers.GetValues("Wechatpay-Timestamp").First();
                var wechatpayNonce = responseMessage.Headers.GetValues("Wechatpay-Nonce").First();
                var wechatpaySignature = responseMessage.Headers.GetValues("Wechatpay-Signature").First();

                result.Signed = TenPaySignHelper.VerifyTenpaySign(wechatpayTimestamp, wechatpayNonce, wechatpaySignature, content);

                result = content.GetObject<T>();
            }
            else
            {
                result = new();
            }
            //T result = resutlCode.Success ? (await responseMessage.Content.ReadAsStringAsync()).GetObject<T>() : new T();
            result.ResultCode = resutlCode;

            return result;
        }
    }
}