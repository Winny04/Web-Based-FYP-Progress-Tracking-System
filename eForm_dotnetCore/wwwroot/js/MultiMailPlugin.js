/*
###############################
##          Author           ##
##      Beren İlkim Ceylan   ##
## bceylanues[at]gmail[d]com ##
##      @berenceylan         ##
###############################
*/

(function ($) {
	// Collects mails as an array
    var result = [];
    // First input
    var input;
    // Collect mails as CSV in a form invisibly.
    var hiddeninput;
    // Default settings
    var defaults;

    $.fn.multiMailInput = function (options) {
        var $this = this;

        defaults = {
        	// Images directory
            imageDir: "image/",
            // Error language settings
            validation_error: "is not valid!",
            duplicate_error: "is already exist!",
            placeholder: "Press comma or tab to add e-mails"
        };
		
		//Initializing options
        if(typeof options !== "undefined"){
            if(typeof options.imageDir !== "undefined"){
                defaults.imageDir = options.imageDir;
            }
            if(typeof options.validation_error !== "undefined"){
                defaults.validation_error = options.validation_error;
            }
            if(typeof options.duplicate_error !== "undefined"){
                defaults.duplicate_error = options.duplicate_error;
            }
            if(typeof options.placeholder !== "undefined"){
                defaults.placeholder = options.placeholder;
            }
        }

        $this.addMail = function (mail, elem) {
            var condition = validateMail(mail, result)["condition"];
            var msg = validateMail(mail, result)["msg"];
            if(condition){
                elem.siblings(".mmBoxes").append("<span class='mmBox' style='height: 24px; line-height:24px;vertical-align: center; width: 300px;'>" + mail + " <img class='deleteBox' width='24' src='"+defaults.imageDir+"delete.png' style='cursor: pointer; float: right;'/></span>");
                $.fn.multiMailInput.arrangeBoxCss(".mmBox");
                result.push(mail);
                input.val("");
                hiddeninput.val($this.getMailsCSV());
                $(".deleteBox").click(function () {
                    deleteMMBox(this);
                });
            }else{
               alert(msg)
            }
            
        },
        $this.addMailsArray = function (mailArray, elem) {
            for (var i in mailArray) {
                $this.addMail(mailArray[i],elem);
            }
        },
        $this.addMailsCsv = function (mailString, delimeter, elem) {
            var mailArray = mailString.split(delimeter);
            for (var i in mailArray) {
                $this.addMail(mailArray[i],elem);
            }
        },
        $this.getMailsCSV = function () {
            var CSV = "";
            for (var i in result) {
                CSV += result[i];
                //If it is not the last item add comma
                if(i != result.length-1){
                     CSV += ",";
                }
            }
            return CSV;
        },
        // Constructor    
        this.filter(".multiMail").each(function () {
            input = $(this);
            hiddeninput = input.siblings(".multiMailResult");
            hiddeninput.css("display", "none");
            input.css("position", "relative");
            input.css("margin-top", "10px");
            input.css("margin-left", "10px");
            input.parent(".mmOuter").css("margin-top", "20px");
            input.attr("placeholder", defaults.placeholder);
            input.keydown(function (event) {
            	//Comma or tab key
                if (event.keyCode === 188 || event.keyCode === 9) {
                    event.preventDefault();
                    var raw = input.val();
                    var mails = raw.split(",");
                    
                    for (var i in mails){
                        if(typeof mails[i] !== "undefined")
                            $this.addMail(mails[i], input);
                    }
                }
            });
        });

        return {
            getMails: function () {
                return result;
            },
            addMail: function(mail){
                $this.addMail(mail, input);
            },
            addMailArray: function(array){
                $this.addMailsArray(array, input);
            },
            addMailCSV: function(mailString, delimeter){
                $this.addMailsCsv(mailString, delimeter, input);
            },
            getMailsCSV: function(){
                return $this.getMailsCSV();
            }
        };
    };

    function validateMail(mail, array) {
        // returns boolean after validating array of emails
        var msg = "";
        //Regex to test emails
        var re = /^(([^<>()\[\]\\.,;:\s@"]+(\.[^<>()\[\]\\.,;:\s@"]+)*)|(".+"))@((\[[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}])|(([a-zA-Z\-0-9]+\.)+[a-zA-Z]{2,}))$/;
        condition = true;
        if (!re.test(mail)) {
            condition = false;
            msg = "\""+ mail + "\" is not valid!";
        }
        if (array.indexOf(mail) !== -1) {
            condition = false;
            msg += "\""+ mail + "\" is already exist!";
        }
        return {
            "condition": condition,
            "msg": msg
        };
    }

    function deleteMMBox(elem) {
        var itemToDel = $(elem).parent(".mmBox").text().replace(/ /g, '');
        while (result.indexOf(itemToDel) !== -1) {
            result.splice(result.indexOf(itemToDel), 1);
        }
        $(elem).parent(".mmBox").remove();
    }

    $.fn.multiMailInput.arrangeBoxCss = function (elem) {
        e = $(elem);
        e.css("margin", "10px");
        e.css("padding", "5px");
        e.css("border", "1px solid #B9B9B9");
        e.css("border-radius", "2px");
        e.css("background-color", "#F5F5F5");
        e.css("color", "black");
        e.css("display", "block");
        e.css("display", "block");
    };

}(jQuery));
