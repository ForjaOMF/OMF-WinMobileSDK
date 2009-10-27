using System;
using System.Net;
using System.IO;
using System.Text;
using System.Xml;
using System.Collections;

namespace SMS20Api
{
    /// <summary>
    /// Utility class for contact retrieving using movistar web service
    /// </summary>
    class SMS20
    {
        private string _lastError = string.Empty;
        private string _sessionId;
        private string _alias;
        private int _transactionId = 0;

        /// <summary>
        /// </summary>
        /// <returns>When some request returns false, returns the error description</returns>
        public string GetLastError() { return _lastError; }

        /// <summary>
        /// Performs login to movistar web site
        /// </summary>
        /// <param name="login">string with user's telephone number</param>
        /// <param name="pwd">string with user's password</param>
        /// <returns>Session Id </returns>
        public string Login(string login, string pwd)
        {
            try
            {
                string loginData = string.Format(
                    "TM_ACTION=AUTHENTICATE&TM_PASSWORD={1}&SessionCookie=ColibriaIMPS_367918656&TM_LOGIN={0}&ClientID=WV%3AInstantMessenger-1.0.2309.16485%40COLIBRIA.PC-CLIENT",
                    login,
                    pwd
                   );

                HttpWebResponse response = HttpHelper.ExecuteRequest(
                    "http://impw.movistar.es/tmelogin/tmelogin.jsp",
                    "application/x-www-form-urlencoded",
                    "POST",
                    loginData,
                    null,
                    false
                    );

                if (response == null)
                {
                    _lastError = "Unable to contact web service";
                    return null;
                }

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    _lastError = "Server error: " + response.StatusCode.ToString() + " " + response.StatusDescription;
                    return null;
                }

                XmlDocument doc = HttpHelper.ReadBodyAsXml(response);
                if (doc == null)
                {
                    _lastError = "Bad xml";
                    return null;
                }
                if (!readResultNode(doc, "Login-Response"))
                    return null;

                XmlElement nodeSession = getXmlNode(doc, "//msg:WV-CSP-Message//msg:Session//msg:Transaction//trans:TransactionContent//trans:Login-Response//trans:SessionID");
                if (nodeSession == null)
                {
                    _lastError = "Unable to read sessionId";
                    return null;
                }
                _sessionId = nodeSession.InnerText;
                return _sessionId;
            }
            catch (Exception ex)
            {
                _lastError = "MAIN EX: " + ex.Message;
                return null;
            }
        }

        /// <summary>
        /// Connects to SMS2.0 service
        /// </summary>
        /// <param name="log">string with user's telephone number</param>
        /// <param name="nickName">string with the nickname that we want to use</param>
        /// <returns>Contact list of the user (arralist of Contact class</returns>
        public ArrayList Connect(string log, string nickName)
        {

            ArrayList contacts = new ArrayList();

            try
            {
                XmlDocument retDoc = null;
                
                // ClientCapability-Request
                retDoc = doSMS20Request(getCapabilityData());
                if (retDoc == null)
                    return null;

                // Service-Request
                retDoc = doSMS20Request(getServiceData());
                if (retDoc == null)
                    return null;


                // UpdatePresence-Request
                retDoc = doSMS20Request(getUpdatePresenceData());
                if (retDoc == null || !readResultNode(retDoc, "Status"))
                    return null;

                // GetList-Request
                retDoc = doSMS20Request(getListData());
                if (retDoc == null)
                    return null;

                bool contactListExists = false;
                foreach (XmlElement node in getXmlNodes(retDoc, "//msg:WV-CSP-Message//msg:Session//msg:Transaction//trans:TransactionContent//trans:GetList-Response//trans:ContactList"))
                {
                    if (node.InnerText == "wv:" + log + "/~pep1.0_subscriptions@movistar.es")
                    {
                        contactListExists = true;
                        break;
                    }
                }

                //GetPresence-Request
                retDoc = doSMS20Request(getPresenceData("wv:" + log + "@movistar.es"));
                if (retDoc == null || !readResultNode(retDoc, "GetPresence-Response"))
                    return null;

                XmlElement nodeAlias = getXmlNode(retDoc, "//msg:WV-CSP-Message//msg:Session//msg:Transaction//trans:TransactionContent//trans:GetPresence-Response//trans:Presence//pres:PresenceSubList//pres:Alias//pres:PresenceValue");
                if (nodeAlias == null)
                {
                    _lastError = "Alias not found";
                    return null;
                }
                _alias = nodeAlias.InnerText;


                //ListManage-Request
                retDoc = doSMS20Request(getListManageData(log));
                if (retDoc == null || !readResultNode(retDoc, "ListManage-Response"))
                    return null;

                foreach (XmlElement nodeContact in getXmlNodes(retDoc, "//msg:WV-CSP-Message//msg:Session//msg:Transaction//trans:TransactionContent//trans:ListManage-Response//trans:NickList//trans:NickName"))
                {
                    string alias = null;
                    string id = null;
                    foreach (XmlElement node in nodeContact.ChildNodes)
                    {
                        switch (node.Name)
                        {
                            case "Name":
                                alias = node.InnerText;
                                break;
                            case "UserID":
                                id = node.InnerText;
                                break;
                        }
                    }
                    Contact contact = new Contact(alias, id);
                    contacts.Add(contact);
                }

                if (!contactListExists)
                {
                    // CreateList-Request (If not exists)
                    retDoc = doSMS20Request(getCreateListData(log, contacts));
                    if (retDoc == null || !readResultNode(retDoc, "Status"))
                        return null;
                }

                // SubscribePresence-Request
                retDoc = doSMS20Request(getSuscribePresenceData(log));
                if (retDoc == null || !readResultNode(retDoc, "Status"))
                    return null;
                

                if (nickName != _alias)
                {
                    // UpdatePresence-Request (If out nick is different than the last one used)
                    retDoc = doSMS20Request(getLiteUpdatePresenceData(nickName));
                    if (retDoc == null || !readResultNode(retDoc, "Status"))
                        return null;
                    _alias = nickName;
                }
                return contacts;
            }
            catch (Exception ex)
            {
                _lastError = "MAIN EX: " + ex.Message;
                return null;
            }
        }

        /// <summary>
        /// Performs polling to search for new message notifications, contacts online, etc...
        /// </summary>
        /// <returns>Full text of the response to search for different types of notification</returns>
        public string Polling()
        {

            try
            {
                XmlDocument retDoc = null;
                bool emptyResponse = false;

                // Polling-Request
                retDoc = doSMS20Request(getPoolData(), ref emptyResponse);
                if (!emptyResponse)
                {
                    if (retDoc == null)
                        return null;

                    XmlNamespaceManager nsmgr = new XmlNamespaceManager(retDoc.NameTable);
                    nsmgr.AddNamespace("msg", "http://www.openmobilealliance.org/DTD/WV-CSP1.2");
                    nsmgr.AddNamespace("trans", "http://www.openmobilealliance.org/DTD/WV-TRC1.2");
                    XmlElement nodeResult = (XmlElement)retDoc.SelectSingleNode("//msg:WV-CSP-Message//msg:Session//msg:Transaction//trans:TransactionContent", nsmgr);
                    if (nodeResult != null && nodeResult.ChildNodes.Count > 0)
                        return nodeResult.ChildNodes[0].OuterXml;
                    else
                    {
                        _lastError = "Unspected xml data";
                        return null;
                    }
                }
                else
                    return "";

            }
            catch (Exception ex)
            {
                _lastError = "MAIN EX: " + ex.Message;
                return null;
            }
        }

        /// <summary>
        /// Adds a contact to the user's contact list
        /// </summary>
        /// <param name="log">string with user's telephone number</param>
        /// <param name="contact">string with new contact's telephone number</param>
        /// <returns>nickname of the new contact</returns>
        public string AddContact(string log,string contact)
        {
            try
            {
                XmlDocument retDoc = null;

                // Search-Request
                retDoc = doSMS20Request(getSearchData(contact));
                if (retDoc == null)
                    return null;

                XmlElement nodeId = getXmlNode(retDoc, "//msg:WV-CSP-Message//msg:Session//msg:Transaction//trans:TransactionContent//trans:Search-Response//trans:SearchResult//trans:UserList//trans:User//trans:UserID");
                if (nodeId == null)
                {
                    _lastError = "User not found";
                    return null;
                }
                string userId = nodeId.InnerText;

                // GetPresence-Request
                retDoc = doSMS20Request(getPresenceData(userId));
                if (retDoc == null)
                    return null;

                XmlElement nodeAlias = getXmlNode(retDoc, "//msg:WV-CSP-Message//msg:Session//msg:Transaction//trans:TransactionContent//trans:GetPresence-Response//trans:Presence//pres:PresenceSubList//pres:Alias//pres:PresenceValue");
                if (nodeAlias == null)
                {
                    _lastError = "Alias not found";
                    return null;
                }
                string nickName = nodeAlias.InnerText;

                // ListManage-Request for Subscriptions
                retDoc = doSMS20Request(getListManageAddUserToSubscriptionData(log,nickName, userId));
                if (retDoc == null || !readResultNode(retDoc, "ListManage-Response"))
                    return null;


                // ListManage-Request for PrivateList
                retDoc = doSMS20Request(getListManageAddUserToPrivateListData(log,nickName, userId));
                if (retDoc == null || !readResultNode(retDoc, "ListManage-Response"))
                    return null;
                
                return nickName;
            }
            catch (Exception ex)
            {
                _lastError = "MAIN EX: " + ex.Message;
                return null;
            }
        }

        /// <summary>
        /// Authorizes a contact to be informed about our presence status
        /// </summary>
        /// <param name="userId">user id for the authorized contact (wv:6yyyyyyyy@movistar.es)</param>
        /// <returns></returns>
        public bool AuthorizeContact(string userId)
        {
            try
            {
                XmlDocument retDoc = null;

                // GetPresence-Request
                retDoc = doSMS20Request(getPresenceData(userId));
                if (retDoc == null || !readResultNode(retDoc, "GetPresence-Response"))
                    return false;

                // Status Ack to the request
                retDoc = doSMS20Request(getAckData());
                if (retDoc == null || !readResultNode(retDoc, "Status"))
                    return false;

                // PresenceAuth-User
                retDoc = doSMS20Request(getAuthRequestData(userId));
                if (retDoc == null || !readResultNode(retDoc, "Status"))
                    return false;
                
                return true;
            }
            catch (Exception ex)
            {
                _lastError = "MAIN EX: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Deletes a contact
        /// </summary>
        /// <param name="log">string with user's telephone number</param>
        /// <param name="contact">user id for the contact to delete (wv:6yyyyyyyy@movistar.es)</param>
        /// <returns></returns>
        public bool DeleteContact(string log,string contact)
        {
            try
            {
                XmlDocument retDoc = null;

                // ListManage-Request to remove from Subscriptions
                retDoc = doSMS20Request(getListManageRemoveUserFromSubscriptionsData(log,contact));
                if (retDoc == null)
                    return false;
                if (!readResultNode(retDoc, "ListManage-Response"))
                {
                    //WriteLog("WARNING. Unable to remove user from Subscriptions: " + _lastError);
                }

                // ListManage-Request to remove from PrivateList
                retDoc = doSMS20Request(getListManageRemoveUserFromPrivateListData(log,contact));
                if (retDoc == null)
                    return false;

                if (!readResultNode(retDoc, "ListManage-Response"))
                {
                    //WriteLog("WARNING. Unable to remove user from Private List: " + _lastError);
                }

                // UnsubscribePresence-Request
                retDoc = doSMS20Request(getUnsubscribePresenceData(contact));
                if (retDoc == null)
                    return false;

                if (!readResultNode(retDoc, "Status"))
                {
                    //WriteLog("WARNING. Unable to remove subscription presence: " + _lastError);
                }


                // DeleteAttributeList-Request
                retDoc = doSMS20Request(getDeleteAttributeListData(contact));
                if (retDoc == null)
                    return false;

                if (!readResultNode(retDoc, "Status"))
                {
                    //WriteLog("WARNING. Unable to remove attribute list: " + _lastError);
                }

                // CancelAuth-Request
                retDoc = doSMS20Request(getCancelAuthData(contact));
                if (retDoc == null)
                    return false;

                if (!readResultNode(retDoc, "Status"))
                {
                    //WriteLog("WARNING. Unable to cancel authorization: " + _lastError);
                }

                return true;
            }
            catch (Exception ex)
            {
                _lastError = "MAIN EX: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Sends a message to the destination contact
        /// </summary>
        /// <param name="log">string with user's telephone number</param>
        /// <param name="destination">string with the destination user id (wv:6xxxxxxxx@movistar.es)</param>
        /// <param name="message">text of the message</param>
        /// <returns></returns>
        public bool SendMessage(string log, string destination, string message)
        {
            try
            {
                XmlDocument retDoc = null;

                // SendMessage-Request
                retDoc = doSMS20Request(getMessageData(log,destination,message));

                if (retDoc == null || !readResultNode(retDoc, "SendMessage-Response"))
                    return false;

                return true;
            }
            catch (Exception ex)
            {
                _lastError = "MAIN EX: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Performs disconnect from SMS2.0 service
        /// </summary>
        /// <returns></returns>
        public bool Disconnect()
        {
            try
            {
                XmlDocument retDoc = null;

                // Logout-Request
                retDoc = doSMS20Request(getLogoutData());

                if (retDoc == null || !readResultNode(retDoc, "Status"))
                    return false;
                
                return true;
            }
            catch (Exception ex)
            {
                _lastError = "MAIN EX: " + ex.Message;
                return false;
            }
        }

        #region Auxiliary functions for polling data parsing
        
        public Contact ParsePollPresenceNotification(string data)
        {
            XmlDocument doc = new XmlDocument();
            doc.LoadXml(data);
            XmlElement nodeId = getXmlNode(doc, "//trans:PresenceNotification-Request//trans:Presence//trans:UserID");
            XmlElement nodeStatus = getXmlNode(doc, "//trans:PresenceNotification-Request//trans:Presence//pres:PresenceSubList//pres:UserAvailability//pres:PresenceValue");
            XmlElement nodeAlias = getXmlNode(doc, "//trans:PresenceNotification-Request//trans:Presence//pres:PresenceSubList//pres:Alias//pres:PresenceValue");

            string id = "";
            string alias = "";
            string status = "";
            if (nodeId == null)
                return null;
            id = nodeId.InnerText;
            if (nodeAlias != null)
                alias = nodeAlias.InnerText;
            if (nodeStatus != null)
                status = nodeStatus.InnerText;

            Contact contact = new Contact(alias, id);
            switch (status.ToUpper())
            {
                case "AVAILABLE":
                    contact.Status = Contact.ContactStatusEnum.online;
                    break;
                default:
                    contact.Status = Contact.ContactStatusEnum.offline;
                    break;
            }
            return contact;
        }
        public Contact ParsePollPresenceAuthNotification(string data)
        {
            XmlDocument doc = new XmlDocument();
            doc.LoadXml(data);
            XmlElement nodeId = getXmlNode(doc, "//trans:PresenceAuth-Request//trans:UserID");
            Contact contact = new Contact("", nodeId.InnerText, Contact.ContactStatusEnum.authPending);
            return contact;
        }
        public InstantMessage ParsePollMessageNotification(string data)
        {
            XmlDocument doc = new XmlDocument();
            doc.LoadXml(data);
            XmlElement nodeSender = getXmlNode(doc, "//trans:NewMessage//trans:MessageInfo//trans:Sender//trans:User//trans:UserID");
            XmlElement nodeStamp = getXmlNode(doc, "//trans:NewMessage//trans:MessageInfo//trans:DateTime");
            XmlElement nodeData = getXmlNode(doc, "//trans:NewMessage//trans:ContentData");

            string sender = "";
            DateTime stamp = DateTime.MinValue;
            string body = "";
            if (nodeSender != null)
                sender = nodeSender.InnerText;
            if (nodeStamp != null)
            {
                string sStamp = nodeStamp.InnerText;
                //20080522T105626
                stamp = new DateTime(
                    int.Parse(sStamp.Substring(0, 4)),
                    int.Parse(sStamp.Substring(4, 2)),
                    int.Parse(sStamp.Substring(6, 2)),
                    int.Parse(sStamp.Substring(9, 2)),
                    int.Parse(sStamp.Substring(11, 2)),
                    int.Parse(sStamp.Substring(13, 2))
                );
            }
            if (nodeData != null)
                body = nodeData.InnerText;

            return new InstantMessage(sender,stamp,body);
        }

        #endregion
        
        #region XmlRequestBuilders

        private string getCapabilityData()
        {
            System.Text.StringBuilder sb = new StringBuilder();

            sb.Append(getCommonHeaderData());

            sb.Append("                <ClientCapability-Request>\r\n");
            sb.Append("                    <ClientID>\r\n");
            sb.Append("                        <URL>WV:InstantMessenger-1.0.2309.16485@COLIBRIA.PC-CLIENT</URL>\r\n");
            sb.Append("                    </ClientID>\r\n");
            sb.Append("                    <CapabilityList>\r\n");
            sb.Append("                        <ClientType>COMPUTER</ClientType>\r\n");
            sb.Append("                        <InitialDeliveryMethod>P</InitialDeliveryMethod>\r\n");
            sb.Append("                        <AcceptedContentType>text/plain</AcceptedContentType>\r\n");
            sb.Append("                        <AcceptedContentType>text/html</AcceptedContentType>\r\n");
            sb.Append("                        <AcceptedContentType>image/png</AcceptedContentType>\r\n");
            sb.Append("                        <AcceptedContentType>image/jpeg</AcceptedContentType>\r\n");
            sb.Append("                        <AcceptedContentType>image/gif</AcceptedContentType>\r\n");
            sb.Append("                        <AcceptedContentType>audio/x-wav</AcceptedContentType>\r\n");
            sb.Append("                        <AcceptedContentType>image/jpg</AcceptedContentType>\r\n");
            sb.Append("                        <AcceptedTransferEncoding>BASE64</AcceptedTransferEncoding>\r\n");
            sb.Append("                        <AcceptedContentLength>256000</AcceptedContentLength>\r\n");
            sb.Append("                        <MultiTrans>1</MultiTrans>\r\n");
            sb.Append("                        <ParserSize>300000</ParserSize>\r\n");
            sb.Append("                        <SupportedCIRMethod>STCP</SupportedCIRMethod>\r\n");
            sb.Append("                        <ColibriaExtensions>T</ColibriaExtensions>\r\n");
            sb.Append("                    </CapabilityList>\r\n");
            sb.Append("                </ClientCapability-Request>\r\n");

            sb.Append(getCommonFooterData());

            return sb.ToString();
        }
        private string getServiceData()
        {
            System.Text.StringBuilder sb = new StringBuilder();

            sb.Append(getCommonHeaderData());

            sb.Append("		            <Service-Request>\r\n");
            sb.Append("                    <ClientID>\r\n");
            sb.Append("                        <URL>WV:InstantMessenger-1.0.2309.16485@COLIBRIA.PC-CLIENT</URL>\r\n");
            sb.Append("                    </ClientID>\r\n");
            sb.Append("                    <Functions>\r\n");
            sb.Append("                        <WVCSPFeat>\r\n");
            sb.Append("                            <FundamentalFeat/>\r\n");
            sb.Append("                            <PresenceFeat/>\r\n");
            sb.Append("                            <IMFeat/>\r\n");
            sb.Append("                            <GroupFeat/>\r\n");
            sb.Append("                        </WVCSPFeat>\r\n");
            sb.Append("                    </Functions>\r\n");
            sb.Append("                    <AllFunctionsRequest>T</AllFunctionsRequest>\r\n");
            sb.Append("                </Service-Request>\r\n");


            sb.Append(getCommonFooterData());

            return sb.ToString();
        }
        private string getUpdatePresenceData()
        {
            System.Text.StringBuilder sb = new StringBuilder();

            sb.Append(getCommonHeaderData());

            sb.Append("		            <UpdatePresence-Request>\r\n");
            sb.Append("                    <PresenceSubList xmlns=\"http://www.openmobilealliance.org/DTD/WV-PA1.2\">\r\n");
            sb.Append("                        <OnlineStatus>\r\n");
            sb.Append("                            <Qualifier>T</Qualifier>\r\n");
            sb.Append("                        </OnlineStatus>\r\n");
            sb.Append("                        <ClientInfo>\r\n");
            sb.Append("                            <Qualifier>T</Qualifier>\r\n");
            sb.Append("                            <ClientType>COMPUTER</ClientType>\r\n");
            sb.Append("                            <ClientTypeDetail xmlns=\"http://imps.colibria.com/PA-ext-1.2\">PC</ClientTypeDetail>\r\n");
            sb.Append("                            <ClientProducer>Colibria As</ClientProducer>\r\n");
            sb.Append("                            <Model>TELEFONICA Messenger</Model>\r\n");
            sb.Append("                            <ClientVersion>1.0.2309.16485</ClientVersion>\r\n");
            sb.Append("                        </ClientInfo>\r\n");
            sb.Append("                        <CommCap>\r\n");
            sb.Append("                            <Qualifier>T</Qualifier>\r\n");
            sb.Append("                            <CommC>\r\n");
            sb.Append("                                <Cap>IM</Cap>\r\n");
            sb.Append("                                <Status>OPEN</Status>\r\n");
            sb.Append("                            </CommC>\r\n");
            sb.Append("                        </CommCap>\r\n");
            sb.Append("                        <UserAvailability>\r\n");
            sb.Append("                            <Qualifier>T</Qualifier>\r\n");
            sb.Append("                            <PresenceValue>AVAILABLE</PresenceValue>\r\n");
            sb.Append("                        </UserAvailability>\r\n");
            sb.Append("                    </PresenceSubList>\r\n");
            sb.Append("                </UpdatePresence-Request>\r\n");


            sb.Append(getCommonFooterData());

            return sb.ToString();
        }
        private string getListData()
        {
            System.Text.StringBuilder sb = new StringBuilder();

            sb.Append(getCommonHeaderData());
            sb.Append("		            <GetList-Request/>\r\n");
            sb.Append(getCommonFooterData());

            return sb.ToString();
        }
        private string getPresenceData(string userId)
        {
            System.Text.StringBuilder sb = new StringBuilder();

            sb.Append(getCommonHeaderData());
            sb.Append("		            <GetPresence-Request>\r\n");
            sb.Append("		                <User>\r\n");
            sb.Append("		                    <UserID>" + userId + "</UserID>\r\n");
            sb.Append("		                </User>\r\n");
            sb.Append("                     <PresenceSubList xmlns=\"http://www.openmobilealliance.org/DTD/WV-PA1.2\">\r\n");
            sb.Append("                         <OnlineStatus/>\r\n");
            sb.Append("                         <ClientInfo/>\r\n");
            sb.Append("                         <GeoLocation/>\r\n");
            sb.Append("                         <FreeTextLocation/>\r\n");
            sb.Append("                         <CommCap/>\r\n");
            sb.Append("                         <UserAvailability/>\r\n");
            sb.Append("                         <StatusText/>\r\n");
            sb.Append("                         <StatusMood/>\r\n");
            sb.Append("                         <Alias/>\r\n");
            sb.Append("                         <StatusContent/>\r\n");
            sb.Append("                         <ContactInfo/>\r\n");
            sb.Append("                     </PresenceSubList>\r\n");
            sb.Append("		            </GetPresence-Request>\r\n");

            sb.Append(getCommonFooterData());

            return sb.ToString();
        }
        private string getListManageData(string phoneNumber)
        {
            System.Text.StringBuilder sb = new StringBuilder();

            sb.Append(getCommonHeaderData());

            sb.Append("		            <ListManage-Request>\r\n");
            sb.Append("		                <ContactList>wv:" + phoneNumber + "/~pep1.0_privatelist@movistar.es</ContactList>\r\n");
            sb.Append("		                <ReceiveList>T</ReceiveList>\r\n");
            sb.Append("		            </ListManage-Request>\r\n");

            sb.Append(getCommonFooterData());

            return sb.ToString();
        }
        private string getCreateListData(string phoneNumber,System.Collections.ArrayList contacts)
        {
            System.Text.StringBuilder sb = new StringBuilder();

            sb.Append(getCommonHeaderData());
            sb.Append("		            <CreateList-Request>\r\n");
            sb.Append("		                <ContactList>wv:" + phoneNumber + "/~PEP1.0_subscriptions@movistar.es</ContactList>\r\n");
            sb.Append("		                <NickList>\r\n");

            foreach (Contact contact in contacts)
            {
                sb.Append("		                    <NickName>\r\n");
                sb.Append("		                        <Name>" + contact.Alias + "</Name>\r\n");
                sb.Append("		                        <UserID>" + contact.Id + "</UserID>\r\n");
                sb.Append("		                    </NickName>\r\n");
            }
            sb.Append("		                </NickList>\r\n");
            sb.Append("		            </CreateList-Request>\r\n");

            sb.Append(getCommonFooterData());

            return sb.ToString();
        }
        private string getSuscribePresenceData(string phoneNumber)
        {
            System.Text.StringBuilder sb = new StringBuilder();

            sb.Append(getCommonHeaderData());

            sb.Append("                <SubscribePresence-Request>\r\n");
            sb.Append("                    <ContactList>wv:" + phoneNumber + "/~PEP1.0_subscriptions@movistar.es</ContactList>\r\n");
            sb.Append("                    <PresenceSubList xmlns=\"http://www.openmobilealliance.org/DTD/WV-PA1.2\">\r\n");
            sb.Append("                        <OnlineStatus/>\r\n");
            sb.Append("                        <ClientInfo/>\r\n");
            sb.Append("                        <FreeTextLocation/>\r\n");
            sb.Append("                        <CommCap/>\r\n");
            sb.Append("                        <UserAvailability/>\r\n");
            sb.Append("                        <StatusText/>\r\n");
            sb.Append("                        <StatusMood/>\r\n");
            sb.Append("                        <Alias/>\r\n");
            sb.Append("                        <StatusContent/>\r\n");
            sb.Append("                        <ContactInfo/>\r\n");
            sb.Append("                    </PresenceSubList>\r\n");
            sb.Append("                    <AutoSubscribe>T</AutoSubscribe>\r\n");
            sb.Append("                </SubscribePresence-Request>\r\n");

            sb.Append(getCommonFooterData());


            return sb.ToString();
        }
        private string getLiteUpdatePresenceData(string nickname)
        {
            System.Text.StringBuilder sb = new StringBuilder();

            sb.Append(getCommonHeaderData());

            sb.Append("                <UpdatePresence-Request>\r\n");
            sb.Append("                    <PresenceSubList xmlns=\"http://www.openmobilealliance.org/DTD/WV-PA1.2\">\r\n");
            sb.Append("                        <Alias>\r\n");
            sb.Append("                            <Qualifier>T</Qualifier>\r\n");
            sb.Append("                            <PresenceValue>" + nickname + "</PresenceValue>\r\n");
            sb.Append("                        </Alias>\r\n");
            sb.Append("                    </PresenceSubList>\r\n");
            sb.Append("                </UpdatePresence-Request>\r\n");



            sb.Append(getCommonFooterData());


            return sb.ToString();
        }
        private string getPoolData()
        {
            System.Text.StringBuilder sb = new StringBuilder();
            sb.Append(getCommonHeaderData());
            sb.Append("                <Polling-Request />\r\n");
            sb.Append(getCommonFooterData());
            return sb.ToString();
        }

        private string getAckData()
        {
            System.Text.StringBuilder sb = new StringBuilder();
            sb.Append(getCommonHeaderData());
            sb.Append("                <Status>\r\n");
            sb.Append("                     <Result>\r\n");
            sb.Append("                         <Code>200</Code>\r\n");
            sb.Append("                     </Result>\r\n");
            sb.Append("                </Status>\r\n");
            sb.Append(getCommonFooterData());
            return sb.ToString();
        }
        private string getAuthRequestData(string userId)
        {
            System.Text.StringBuilder sb = new StringBuilder();
            sb.Append(getCommonHeaderData());
            sb.Append("                <PresenceAuth-User>\r\n");
            sb.Append("                     <UserID>" + userId + "</UserID>\r\n");
            sb.Append("                     <Acceptance>T</Acceptance>\r\n");
            sb.Append("                </PresenceAuth-User>\r\n");
            sb.Append(getCommonFooterData());
            return sb.ToString();
        }
      
        private string getListManageAddUserToPrivateListData(string log, string nick, string userId)
        {
            System.Text.StringBuilder sb = new StringBuilder();

            sb.Append(getCommonHeaderData());

            sb.Append("		            <ListManage-Request>\r\n");
            sb.Append("		                <ContactList>wv:" + log + "/~pep1.0_privatelist@movistar.es</ContactList>\r\n");
            sb.Append("                     <AddNickList>\r\n");
            sb.Append("                         <NickName>\r\n");
            sb.Append("                             <Name>" + nick + "</Name>\r\n");
            sb.Append("                             <UserID>" + userId + "</UserID>\r\n");
            sb.Append("                         </NickName>\r\n");
            sb.Append("                     </AddNickList>\r\n");
            sb.Append("		                <ReceiveList>T</ReceiveList>\r\n");
            sb.Append("		            </ListManage-Request>\r\n");

            sb.Append(getCommonFooterData());

            return sb.ToString();
        }
        private string getListManageAddUserToSubscriptionData(string log,string nick, string userId)
        {
            System.Text.StringBuilder sb = new StringBuilder();

            sb.Append(getCommonHeaderData());

            sb.Append("		            <ListManage-Request>\r\n");
            sb.Append("		                <ContactList>wv:" + log + "/~PEP1.0_subscriptions@movistar.es</ContactList>\r\n");
            sb.Append("                     <AddNickList>\r\n");
            sb.Append("                         <NickName>\r\n");
            sb.Append("                             <Name>" + nick + "</Name>\r\n");
            sb.Append("                             <UserID>" + userId + "</UserID>\r\n");
            sb.Append("                         </NickName>\r\n");
            sb.Append("                     </AddNickList>\r\n");
            sb.Append("		                <ReceiveList>T</ReceiveList>\r\n");
            sb.Append("		            </ListManage-Request>\r\n");

            sb.Append(getCommonFooterData());

            return sb.ToString();
        }
        private string getListManageRemoveUserFromSubscriptionsData(string log,string phoneNumber)
        {
            System.Text.StringBuilder sb = new StringBuilder();
            sb.Append(getCommonHeaderData());
            sb.Append("                <ListManage-Request>\r\n");
            sb.Append("                     <ContactList>wv:" + log + "/~PEP1.0_subscriptions@movistar.es</ContactList>\r\n");
            sb.Append("                     <RemoveNickList>\r\n");
            sb.Append("                         <UserID>" + phoneNumber + "</UserID>\r\n");
            sb.Append("                     </RemoveNickList>\r\n");
            sb.Append("                     <ReceiveList>T</ReceiveList>\r\n");
            sb.Append("                </ListManage-Request>\r\n");
            sb.Append(getCommonFooterData());
            return sb.ToString();
        }
        private string getListManageRemoveUserFromPrivateListData(string log,string phoneNumber)
        {
            System.Text.StringBuilder sb = new StringBuilder();
            sb.Append(getCommonHeaderData());
            sb.Append("                <ListManage-Request>\r\n");
            sb.Append("                     <ContactList>wv:" + log + "/~PEP1.0_privatelist@movistar.es</ContactList>\r\n");
            sb.Append("                     <RemoveNickList>\r\n");
            sb.Append("                         <UserID>" + phoneNumber + "</UserID>\r\n");
            sb.Append("                     </RemoveNickList>\r\n");
            sb.Append("                     <ReceiveList>T</ReceiveList>\r\n");
            sb.Append("                </ListManage-Request>\r\n");
            sb.Append(getCommonFooterData());
            return sb.ToString();
        }
        private string getUnsubscribePresenceData(string phoneNumber)
        {
            System.Text.StringBuilder sb = new StringBuilder();
            sb.Append(getCommonHeaderData());
            sb.Append("                <UnsubscribePresence-Request>\r\n");
            sb.Append("                     <User>\r\n");
            sb.Append("                         <UserID>" + phoneNumber + "</UserID>\r\n");
            sb.Append("                     </User>\r\n");
            sb.Append("                </UnsubscribePresence-Request>\r\n");
            sb.Append(getCommonFooterData());
            return sb.ToString();
        }
        private string getDeleteAttributeListData(string phoneNumber)
        {
            System.Text.StringBuilder sb = new StringBuilder();
            sb.Append(getCommonHeaderData());
            sb.Append("                <DeleteAttributeList-Request>\r\n");
            sb.Append("                     <UserID>" + phoneNumber + "</UserID>\r\n");
            sb.Append("                     <DefaultList>F</DefaultList>\r\n");
            sb.Append("                </DeleteAttributeList-Request>\r\n");
            sb.Append(getCommonFooterData());
            return sb.ToString();
        }
        private string getCancelAuthData(string phoneNumber)
        {
            System.Text.StringBuilder sb = new StringBuilder();
            sb.Append(getCommonHeaderData());
            sb.Append("                <CancelAuth-Request>\r\n");
            sb.Append("                     <UserID>" + phoneNumber + "</UserID>\r\n");
            sb.Append("                </CancelAuth-Request>\r\n");
            sb.Append(getCommonFooterData());
            return sb.ToString();
        }
        private string getSearchData(string phoneNumber)
        {
            System.Text.StringBuilder sb = new StringBuilder();
            sb.Append(getCommonHeaderData());
            sb.Append("                <Search-Request>\r\n");
            sb.Append("                     <SearchPairList>\r\n");
            sb.Append("                         <SearchElement>USER_MOBILE_NUMBER</SearchElement>\r\n");
            sb.Append("                         <SearchString>" + phoneNumber + "</SearchString>\r\n");
            sb.Append("                     </SearchPairList>\r\n");
            sb.Append("                     <SearchLimit>50</SearchLimit>\r\n");
            sb.Append("                </Search-Request>\r\n");
            sb.Append(getCommonFooterData());
            return sb.ToString();
        }

        private string getMessageData(string senderId, string targetId, string body)
        {
            System.Text.StringBuilder sb = new StringBuilder();
            sb.Append(getCommonHeaderData());
            sb.Append("                <SendMessage-Request>\r\n");
            sb.Append("                     <DeliveryReport>F</DeliveryReport>\r\n");
            sb.Append("                     <MessageInfo>\r\n");
            sb.Append("                         <ContentType>text/html</ContentType>\r\n");
            sb.Append("                         <ContentSize>" + body.Length.ToString() + "</ContentSize>\r\n");
            sb.Append("                         <Recipient>\r\n");
            sb.Append("                             <User>\r\n");
            sb.Append("                                 <UserID>" + targetId + "</UserID>\r\n");
            sb.Append("                             </User>\r\n");
            sb.Append("                         </Recipient>\r\n");
            sb.Append("                         <Sender>\r\n");
            sb.Append("                             <User>\r\n");
            sb.Append("                                 <UserID>" + senderId + "</UserID>\r\n");
            sb.Append("                             </User>\r\n");
            sb.Append("                         </Sender>\r\n");
            sb.Append("                     </MessageInfo>\r\n");
            sb.Append("                     <ContentData>" + body + "</ContentData>\r\n");
            sb.Append("                 </SendMessage-Request>\r\n");
            
            sb.Append(getCommonFooterData());

            return sb.ToString();
        }
        private string getLogoutData()
        {
            System.Text.StringBuilder sb = new StringBuilder();
            sb.Append(getCommonHeaderData());
            sb.Append("                <Logout-Request/>\r\n");
            sb.Append(getCommonFooterData());
            return sb.ToString();
        }
       
        private string getCommonHeaderData()
        {
            _transactionId++;

            System.Text.StringBuilder sb = new StringBuilder();
            sb.Append("<WV-CSP-Message xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns=\"http://www.openmobilealliance.org/DTD/WV-CSP1.2\">\r\n");
            sb.Append("    <Session>\r\n");
            sb.Append("        <SessionDescriptor>\r\n");
            sb.Append("            <SessionType>Inband</SessionType>\r\n");
            sb.Append("            <SessionID>" + _sessionId + "</SessionID>\r\n");
            sb.Append("        </SessionDescriptor>\r\n");
            sb.Append("        <Transaction>\r\n");
            sb.Append("            <TransactionDescriptor>\r\n");
            sb.Append("                <TransactionMode>Request</TransactionMode>\r\n");
            sb.Append("                <TransactionID>" + _transactionId.ToString() + "</TransactionID>\r\n");
            sb.Append("            </TransactionDescriptor>\r\n");
            sb.Append("            <TransactionContent xmlns=\"http://www.openmobilealliance.org/DTD/WV-TRC1.2\">\r\n");
            return sb.ToString();
        }
        private string getCommonFooterData()
        {
            System.Text.StringBuilder sb = new StringBuilder();
            sb.Append("            </TransactionContent>\r\n");
            sb.Append("        </Transaction>\r\n");
            sb.Append("    </Session>\r\n");
            sb.Append("</WV-CSP-Message>\r\n");
            return sb.ToString();
        }


        #endregion

        #region AuxRequestFunctions
        private XmlDocument doSMS20Request(string data)
        {
            bool temp = false;
            return doSMS20Request(data, ref temp);
        }
        private XmlDocument doSMS20Request(string data, ref bool responseEmpty)
        {
            responseEmpty = false;

            HttpHelper.Header[] headers = new HttpHelper.Header[] { new HttpHelper.Header("Accept-Encoding", "identity"), new HttpHelper.Header("Expect", "100-continue") };
            HttpWebResponse response = HttpHelper.ExecuteRequest(
                "http://sms20.movistar.es/",
                "application/vnd.wv.csp.xml",
                "POST",
                data,
                null,
                false
                );

            if (response == null)
            {
                _lastError = "Unable to contact web service";
                return null;
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                _lastError = "Server error: " + ((int)response.StatusCode).ToString() + " " + response.StatusDescription;
                return null;
            }
            if (response.ContentLength <= 0)
            {
                responseEmpty = true;
                return null;
            }
            else
            {
                XmlDocument doc = HttpHelper.ReadBodyAsXml(response);
                if (doc == null)
                {
                    _lastError = "Bad xml";
                    return null;
                }
                return doc;
            }
        }
        private bool readResultNode(XmlDocument doc, string nodeName)
        {
            string code = "";
            string description = "";
            XmlNamespaceManager nsmgr = new XmlNamespaceManager(doc.NameTable);
            nsmgr.AddNamespace("msg", "http://www.openmobilealliance.org/DTD/WV-CSP1.2");
            nsmgr.AddNamespace("trans", "http://www.openmobilealliance.org/DTD/WV-TRC1.2");

            XmlElement nodeCode = (XmlElement)doc.SelectSingleNode("//msg:WV-CSP-Message//msg:Session//msg:Transaction//trans:TransactionContent//trans:" + nodeName + "//trans:Result//trans:Code", nsmgr);


            if (nodeCode != null)
            {
                code = nodeCode.InnerText;
                if (code.Length==3 && code.StartsWith("2"))
                    return true;
                else
                {
                    XmlElement nodeDescription = (XmlElement)doc.SelectSingleNode("//msg:WV-CSP-Message//msg:Session//msg:Transaction//trans:TransactionContent//trans:" + nodeName + "//trans:Result//trans:Description", nsmgr);
                    if (nodeDescription != null)
                        description = nodeDescription.InnerText;
                    _lastError = code + " " + description;
                    return false;
                }
            }
            else
            {
                _lastError = "Unknow response";
                return false;
            }
        }
        private XmlElement getXmlNode(XmlDocument doc, string xpath)
        {
            XmlNamespaceManager nsmgr = new XmlNamespaceManager(doc.NameTable);
            nsmgr.AddNamespace("msg", "http://www.openmobilealliance.org/DTD/WV-CSP1.2");
            nsmgr.AddNamespace("trans", "http://www.openmobilealliance.org/DTD/WV-TRC1.2");
            nsmgr.AddNamespace("pres", "http://www.openmobilealliance.org/DTD/WV-PA1.2");

            return (XmlElement)doc.SelectSingleNode(xpath,nsmgr);
        }
        private XmlNodeList getXmlNodes(XmlDocument doc, string xpath)
        {
            XmlNamespaceManager nsmgr = new XmlNamespaceManager(doc.NameTable);
            nsmgr.AddNamespace("msg", "http://www.openmobilealliance.org/DTD/WV-CSP1.2");
            nsmgr.AddNamespace("trans", "http://www.openmobilealliance.org/DTD/WV-TRC1.2");
            nsmgr.AddNamespace("pres", "http://www.openmobilealliance.org/DTD/WV-PA1.2");

            return doc.SelectNodes(xpath,nsmgr);
        }

        #endregion

        /// <summary>
        /// Utility class for simplify http parsing
        /// </summary>
        private static class HttpHelper
        {
            /// <summary>
            /// Performs a HTTP request
            /// </summary>
            /// <param name="url">Target URL</param>
            /// <param name="contentType">Content-Type Header</param>
            /// <param name="method">HTTP Method (POST/GET)</param>
            /// <param name="body">string data to send when method is POST</param>
            /// <param name="optionalHeaders">Array of optional headers needed by the request</param>
            /// <param name="autoRedirect">Allows automatically performig autoredirect when the server invokes it</param>
            /// <returns>HttpResponse</returns>
            public static HttpWebResponse ExecuteRequest(string url, string contentType, string method, string body, Header[] optionalHeaders, bool autoRedirect)
            {

                ServicePointManager.CertificatePolicy = new CertificateMovistar();
                HttpWebRequest request = null;

                try
                {

                    request = (HttpWebRequest)WebRequest.Create(url);
                    request.Accept = "image/gif, image/x-xbitmap, image/jpeg, image/pjpeg, application/x-shockwave-flash, application/vnd.ms-excel, application/vnd.ms-powerpoint, application/msword, */*";
                    request.Headers.Add("Accept-Encoding", "gzip, deflate");
                    request.UserAgent = "Mozilla/4.0 (compatible; MSIE 7.0; Windows NT 5.1; .NET CLR 1.1.4322; .NET CLR 2.0.50727)";

                    request.Method = method;
                    request.AllowAutoRedirect = autoRedirect;
                    if (contentType != null && contentType.Length > 0)
                        request.ContentType = contentType;
                    if (body != null)
                        request.ContentLength = (long)body.Length;

                    if (optionalHeaders != null)
                    {
                        foreach (Header header in optionalHeaders)
                            request.Headers.Add(header.Name, header.Value);

                    }

                    if (body != null && body.Length > 0)
                    {
                        Stream stream = request.GetRequestStream();
                        byte[] buffer = System.Text.Encoding.Default.GetBytes(body);
                        stream.Write(buffer, 0, buffer.Length);
                        stream.Flush();
                        stream.Close();
                    }
                }
                catch (Exception ex)
                {
                    //Shared.WriteLog("REQ ERR: " + ex.ToString());
                    return null;
                }
                try
                {
                    HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                    return response;
                }
                catch (WebException webEx)
                {
                    //Shared.WriteLog("HTTP ERR:" + webEx.ToString());
                    return (HttpWebResponse)webEx.Response;
                }
                catch (Exception ex)
                {
                    //Shared.WriteLog("RES ERR:" + ex.ToString());
                    return null;
                }

            }
            public static string ParseCookie(string cookie)
            {
                return "s=" + HttpHelper.ExtractValue(cookie, "s=", ";") + ";skf=" + HttpHelper.ExtractValue(cookie, "skf=", ";");
            }
            /// <summary>
            /// Extract text value between the two provided strings
            /// </summary>
            /// <param name="data">source string</param>
            /// <param name="from">Start string chunk</param>
            /// <param name="to">End string chunk</param>
            /// <returns>If found, return the string. Otherwise return null</returns>
            public static string ExtractValue(string data, string from, string to)
            {
                int i1 = data.IndexOf(from);
                if (i1 > -1)
                {
                    int i2 = data.IndexOf(to, from.Length + i1);
                    if (i2 > -1)
                    {
                        return data.Substring(i1 + from.Length, i2 - i1 - from.Length);
                    }
                }
                return null;
            }
            /// <summary>
            /// Read received body
            /// </summary>
            /// <param name="response">Http response received from the server</param>
            /// <returns>Response body as byte array if available. Otherwise returns null</returns>
            public static byte[] ReadBody(HttpWebResponse response)
            {
                try
                {
                    long lenght = response.ContentLength;
                    MemoryStream ms = new MemoryStream();
                    Stream resStream = response.GetResponseStream();
                    byte[] readBuffer = new byte[8192];
                    while (lenght == -1 || ms.Length < lenght)
                    {
                        int i = resStream.Read(readBuffer, 0, readBuffer.Length);
                        if (i > 0)
                            ms.Write(readBuffer, 0, i);
                        else
                            break;
                    }
                    resStream.Close();
                    try
                    {
                        response.GetResponseStream().Close();
                    }
                    catch { }
                    response.Close();
                    return ms.ToArray();
                }
                catch (Exception ex)
                {
                    //Shared.WriteLog("ERR BODY:" + ex.ToString());
                    response.Close();
                    return null;
                }

            }
            /// <summary>
            /// Read received body
            /// </summary>
            /// <param name="response">Http response received from the server</param>
            /// <param name="encoding">Content-Encoding used to decode the buffer</param>
            /// <returns>Response body as string if available. Otherwise returns null</returns>
            public static string ReadBody(HttpWebResponse response, System.Text.Encoding encoding)
            {
                byte[] data = ReadBody(response);
                return encoding.GetString(data, 0, data.Length);

            }
            public static XmlDocument ReadBodyAsXml(HttpWebResponse response)
            {
                string xml = ReadBody(response, System.Text.Encoding.UTF8);
                if (xml == null)
                    return null;
                XmlDocument doc = new XmlDocument();
                try
                {
                    doc.LoadXml(xml);
                    return doc;
                }
                catch
                {
                    return null;
                }
            }
            public static void Save2disk(string filename, byte[] data)
            {
                FileStream fs = new FileStream(filename, FileMode.Create, FileAccess.Write);
                fs.Write(data, 0, data.Length);
                fs.Close();
                //fs.Dispose();
            }
            /// <summary>
            /// Helper class to store named-value pairs
            /// </summary>
            public class Header
            {
                public string Name;
                public string Value;

                public Header(string name, string value)
                {
                    this.Name = name;
                    this.Value = value;
                }
            }
            /// <summary>
            /// Helper class neccesary for dealing width Movistar SSL certificate (out of date, not signed ..)
            /// </summary>
            private class CertificateMovistar : ICertificatePolicy
            {
                public bool CheckValidationResult(ServicePoint srvPoint, System.Security.Cryptography.X509Certificates.X509Certificate certificate, WebRequest request, int certificateProblem)
                {
                    //Shared.WriteLog("Certificate Acepted");
                    return true;
                }
            }

        }
    }

    /// <summary>
    /// Helper class to store contact properties
    /// </summary>
    public class Contact
    {
        public enum ContactStatusEnum { unknow = -1, offline = 0, online = 1 , authPending = 2}

        public string Alias;
        public string Id;
        public ContactStatusEnum Status = ContactStatusEnum.unknow;
        public string Phone = string.Empty;

        public Contact(string alias, string id)
        {
            this.Alias = alias;
            this.Id = id;
            parsePhone();
        }
        public Contact(string alias, string id, ContactStatusEnum status)
        {
            this.Alias = alias;
            this.Id = id;
            this.Status = status;
            parsePhone();
        }
        private void parsePhone()
        {
            int i1 = this.Id.IndexOf(":");
            if (i1 > -1)
            {
                int i2 = this.Id.IndexOf("@");
                if (i2 > -1)
                    this.Phone = this.Id.Substring(i1 + 1, i2 - i1-1);
            }
        }
    }
    /// <summary>
    /// Helper class to store message properties
    /// </summary>
    public class InstantMessage
    {
        public string Sender;
        public DateTime Received;
        public string Body;

        public InstantMessage(string sender, DateTime received, string body)
        {
            this.Sender = sender;
            this.Received = received;
            this.Body = body;
        }
    }
}
