using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TransparentTwitchChatWPF.Chats
{
    public class YoutubeChat : Chat
    {
        public YoutubeChat() : base(ChatTypes.KapChat)
        {
        }

        public override string SetupJavascript()
        {
            PushNewMessage("Youtube Chat Live: Injecting JavaScript...");

            var script = @"
const jsCallback = chrome.webview.hostObjects.jsCallbackFunctions;

//document.body.style.overflow = ""visible"";
document.querySelector(""#contents"").style.opacity = 0.5;

class ChatController {
  constructor() {
    this.chatMessages = [];
  }

  createChatContainerIfNeeded() {

    // Check if the chat container already exists
    this.chatContainer = document.getElementById(""chat-container"");

    // If not, create a new container
    if (!this.chatContainer) {
      this.chatContainer = document.createElement(""div"");
      this.chatContainer.id = ""chat-container"";


      this.chatContainer.style.position = ""fixed"";
      this.chatContainer.style.top = ""0"";
      this.chatContainer.style.left = ""0"";
      this.chatContainer.style.width = ""100%"";
      this.chatContainer.style.height = ""100%"";
     this.chatContainer.style.maxHeight = ""100%"";
      this.chatContainer.style.overflowY = ""auto"";
        this.chatContainer.style.opacity = ""1"";


      document.body.insertBefore(this.chatContainer, document.body.firstChild);
        this.chatContainer.style.overflowY = ""auto"";
      //document.body.appendChild(this.chatContainer); // Change this line to append to your desired parent element

    }
  }

 renderChatMessage(parsedChatMessage) {
    console.log(""renderChatMessage(parsedChatMessage)"");

        this.createChatContainerIfNeeded();

        const dynamicText = document.createElement('div');


        // Set the style and content of the div
        dynamicText.style.wordWrap = 'break-word';
        dynamicText.style.backgroundColor = 'transparent';
        dynamicText.style.color = 'white';
        dynamicText.style.fontSize = '24px';
dynamicText.style.whiteSpace = ""pre-wrap"";
        dynamicText.style.textShadow = `
  -0   -2px 1px #000000,
		 0   -2px 1px #000000,
		-0    2px 1px #000000,
		 0    2px 1px #000000,
		-2px -0   1px #000000,
		 2px -0   1px #000000,
		-2px  0   1px #000000,
		 2px  0   1px #000000,
		-1px -2px 1px #000000,
		 1px -2px 1px #000000,
		-1px  2px 1px #000000,
		 1px  2px 1px #000000,
		-2px -1px 1px #000000,
		 2px -1px 1px #000000,
		-2px  1px 1px #000000,
		 2px  1px 1px #000000,
		-2px -2px 1px #000000,
		 2px -2px 1px #000000,
		-2px  2px 1px #000000,
		 2px  2px 1px #000000,
		-2px -2px 1px #000000,
		 2px -2px 1px #000000,
		-2px  2px 1px #000000,
		 2px  2px 1px #000000
`;
        dynamicText.style.padding = '10px';
        dynamicText.style.textAlign = 'left';

        const formattedTime = parsedChatMessage.date;
        dynamicText.innerHTML = `${parsedChatMessage.date}, ${parsedChatMessage.nick}, ${parsedChatMessage.message}`;

    this.chatContainer = document.getElementById(""chat-container"");

    // If not, create a new container
    if (!this.chatContainer) {
return;
    }
this.chatContainer.appendChild(dynamicText);

this.chatContainer.scrollTop = this.chatContainer.scrollHeight;

        // Insert the new div at the top of the body
        
        //document.body.insertBefore(dynamicText, document.body.firstChild);


    // Store the chat message
    this.chatMessages.push(parsedChatMessage);

document.querySelector(""#contents"").style.opacity = 0;

  }

  // Method to handle a new message triggered by playSound
  handleNewMessage(parsedChatMessage, isLoud) {
console.log(""AAAAAAAAa"");
if (isLoud) {
jsCallback.playSound(parsedChatMessage.nick, parsedChatMessage.message);
}
    this.renderChatMessage(parsedChatMessage);
  }
}

var chatController = new ChatController();

function handleNewMessage(parsedChatMessage, isLoud) {
console.log(""asdsadsa"");
    chatController.handleNewMessage(parsedChatMessage, isLoud);
    //jsCallback.playSound(parsedChatMessage.nick, parsedChatMessage.message);
}

class ParsedChatMessage {
  constructor(date, nick, message) {
    this.date = date;
    this.nick = nick;
    this.message = message;
  }

  static fromTag(tagElement) {
    const date = tagElement.querySelector('#timestamp').textContent;
    const nick = tagElement.querySelector('#author-name').textContent;
    const message = tagElement.querySelector('#message').textContent;
  
    const parsedMessage = new ParsedChatMessage(date, nick, message);
    return parsedMessage;
  }

  description() {
    return `Date: ${this.date}, Nick: ${this.nick}, Message: ${this.message}`;
  }
}

document.querySelectorAll('.style-scope yt-live-chat-text-message-renderer').forEach((tagElement) => {
  let parsedChatMessage = ParsedChatMessage.fromTag(tagElement);
  handleNewMessage(parsedChatMessage, false);
});

var targetNode = document;
var config = { 
  childList: true, 
  subtree: true, 
  characterData: true, 
  characterDataOldValue: true 
};

var callback = function(mutationsList, observer) {
  for (var mutation of mutationsList) {
    if (mutation.type == 'childList') {
      for (var addedNode of mutation.addedNodes) {
        if (addedNode.id == undefined || addedNode.id == """") {
          return;
        }
        if (addedNode.className != ""style-scope yt-live-chat-item-list-renderer"") {
          return;
        }
        try {
          let parsedChatMessage = ParsedChatMessage.fromTag(addedNode);
            handleNewMessage(parsedChatMessage, true);
            //jsCallback.playSound(parsedChatMessage.nick, parsedChatMessage.message);
          console.log(parsedChatMessage.description())
        }
        catch (error) {
          console.error(error.message);
        }
      }
    }
  }
};

var observer = new MutationObserver(callback);
observer.observe(targetNode, config);";

            return script;
        }
    }
}
